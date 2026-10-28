using IonPackMan.Deployment;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>
/// Tes ServiceControl ASLI hanya membaca / memeriksa error pada service yang tidak ada; tidak ada tes otomatis
/// yang menghentikan atau memulai service sungguhan. Stop/Start asli diuji manual di VM (T31).
/// Keputusan: nama null/kosong/spasi -> ArgumentException (semua method); nama aneh berisi karakter
/// ('\', '/', "..") -> GetState = NotFound, Stop/Start = InvalidOperationException.
/// </summary>
public class ServiceControlTests
{
    private readonly ServiceControl _svc = new();
    private static string Missing() => "NoSuchService-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void GetState_EventLog_IsRunning()
    {
        // EventLog adalah service Windows inti; toleran terhadap StartPending bila mesin baru boot.
        var s = _svc.GetState("EventLog");
        Assert.True(s is ServiceState.Running or ServiceState.StartPending, $"EventLog state: {s}");
    }

    [Fact]
    public void GetState_NameIsCaseInsensitive()
        => Assert.Equal(_svc.GetState("EventLog"), _svc.GetState("eventlog"));

    [Fact]
    public void GetState_Missing_IsNotFound()
        => Assert.Equal(ServiceState.NotFound, _svc.GetState(Missing()));

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("a/b")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("x; del *.*")]
    public void GetState_DangerousName_IsNotFound(string name)
        => Assert.Equal(ServiceState.NotFound, _svc.GetState(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_ThrowsArgumentException(string? name)
    {
        Assert.Throws<ArgumentException>(() => _svc.GetState(name!));
        Assert.Throws<ArgumentException>(() => _svc.Stop(name!, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => _svc.StartAndWaitRunning(name!, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Stop_Missing_ThrowsInvalidOperation()
    {
        var name = Missing();
        var ex = Assert.Throws<InvalidOperationException>(() => _svc.Stop(name, TimeSpan.FromSeconds(1)));
        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void Start_Missing_ThrowsInvalidOperation()
    {
        var name = Missing();
        var ex = Assert.Throws<InvalidOperationException>(() => _svc.StartAndWaitRunning(name, TimeSpan.FromSeconds(1)));
        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void Stop_DangerousName_ThrowsInvalidOperation()
        => Assert.Throws<InvalidOperationException>(() => _svc.Stop("..\\x", TimeSpan.FromSeconds(1)));
}

public class FakeServiceControlTests
{
    private static readonly TimeSpan T = TimeSpan.FromSeconds(5);

    [Fact]
    public void GetState_ReturnsAddedState_OrNotFound()
    {
        var f = new FakeServiceControl().Add("WIND", ServiceState.Running);
        Assert.Equal(ServiceState.Running, f.GetState("WIND"));
        Assert.Equal(ServiceState.Running, f.GetState("wind"));
        Assert.Equal(ServiceState.NotFound, f.GetState("Other"));
    }

    [Fact]
    public void Stop_ChangesStateToStopped()
    {
        var f = new FakeServiceControl().Add("WIND", ServiceState.Running);
        f.Stop("WIND", T);
        Assert.Equal(ServiceState.Stopped, f.GetState("WIND"));
    }

    [Fact]
    public void Start_ChangesStateToRunning()
    {
        var f = new FakeServiceControl().Add("WIND", ServiceState.Stopped);
        f.StartAndWaitRunning("WIND", T);
        Assert.Equal(ServiceState.Running, f.GetState("WIND"));
    }

    [Fact]
    public void StopAndStart_AreIdempotent()
    {
        var f = new FakeServiceControl().Add("A", ServiceState.Stopped).Add("B", ServiceState.Running);
        f.Stop("A", T);
        f.Stop("A", T);
        f.StartAndWaitRunning("B", T);
        f.StartAndWaitRunning("B", T);
        Assert.Equal(ServiceState.Stopped, f.GetState("A"));
        Assert.Equal(ServiceState.Running, f.GetState("B"));
    }

    [Fact]
    public void Calls_RecordOrder()
    {
        var f = new FakeServiceControl().Add("WIND", ServiceState.Running);
        f.GetState("WIND");
        f.Stop("WIND", T);
        f.StartAndWaitRunning("WIND", T);
        Assert.Equal(new[] { "GetState:WIND", "Stop:WIND", "Start:WIND" }, f.Calls);
    }

    [Fact]
    public void Missing_StopAndStart_ThrowInvalidOperation()
    {
        var f = new FakeServiceControl();
        Assert.Throws<InvalidOperationException>(() => f.Stop("X", T));
        Assert.Throws<InvalidOperationException>(() => f.StartAndWaitRunning("X", T));
    }

    [Fact]
    public void NeverStops_ThrowsTimeout_MentioningServiceAndTimeout_StateUnchanged()
    {
        var f = new FakeServiceControl().Add("WIND", ServiceState.Running).NeverStops("WIND");
        var ex = Assert.Throws<TimeoutException>(() => f.Stop("WIND", T));
        Assert.Contains("WIND", ex.Message);
        Assert.Contains("5", ex.Message);
        Assert.Equal(ServiceState.Running, f.GetState("WIND"));
    }

    [Fact]
    public void NeverStops_AlreadyStopped_DoesNotThrow()
    {
        var f = new FakeServiceControl().Add("WIND", ServiceState.Stopped).NeverStops("WIND");
        f.Stop("WIND", T);
    }

    [Fact]
    public void FailOn_Start_ThrowsProgrammedException_StateUnchanged()
    {
        var f = new FakeServiceControl().Add("WIND", ServiceState.Stopped)
            .FailOn("Start", "WIND", new TimeoutException("boom"));
        var ex = Assert.Throws<TimeoutException>(() => f.StartAndWaitRunning("WIND", T));
        Assert.Equal("boom", ex.Message);
        Assert.Equal(ServiceState.Stopped, f.GetState("WIND"));
        Assert.Contains("Start:WIND", f.Calls);
    }

    [Fact]
    public void FailOn_OnlyAffectsTheNamedOperationAndService()
    {
        var f = new FakeServiceControl().Add("A", ServiceState.Running).Add("B", ServiceState.Running)
            .FailOn("Stop", "A", new InvalidOperationException("denied"));
        f.Stop("B", T);
        f.StartAndWaitRunning("A", T);
        Assert.Throws<InvalidOperationException>(() => f.Stop("A", T));
    }
}
