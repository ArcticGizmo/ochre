using Ochre.Core.Claude.Control;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// The in-process <see cref="ControlledSessions"/> registry that tracks the sessions this host drives over
/// stream-json, plus the live status it carries. (Perch's SessionMonitor override tests stay behind until observed
/// sessions are ported.)
/// </summary>
public class ControlledSessionsTests
{
    private static string FreshId() => "controlled-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Changed_FiresOnRealTransitions_NotOnNoOps()
    {
        var id = FreshId();
        int fires = 0;
        // Other test classes register sessions of their own in parallel; count only this one's.
        void OnChanged(string changed)
        {
            if (changed == id) fires++;
        }
        ControlledSessions.Changed += OnChanged;
        try
        {
            ControlledSessions.Register(id);                             // membership change → fires
            Assert.Equal(1, fires);

            ControlledSessions.SetActivity(id, ControlledActivity.Idle); // already idle → no-op, no fire
            Assert.Equal(1, fires);

            ControlledSessions.SetActivity(id, ControlledActivity.Busy); // real change → fires
            Assert.Equal(2, fires);

            ControlledSessions.SetActivity(id, ControlledActivity.Busy); // same again → no fire
            Assert.Equal(2, fires);

            ControlledSessions.Unregister(id);                           // membership change → fires
            Assert.Equal(3, fires);

            ControlledSessions.Unregister(id);                           // already gone → no fire
            Assert.Equal(3, fires);
        }
        finally
        {
            ControlledSessions.Changed -= OnChanged;
            ControlledSessions.Unregister(id);
        }
    }

    [Fact]
    public void Register_Set_Unregister_TracksActivity()
    {
        var id = FreshId();
        try
        {
            Assert.False(ControlledSessions.Owns(id));
            Assert.False(ControlledSessions.Owns(null));
            Assert.Null(ControlledSessions.Activity(id));

            ControlledSessions.Register(id);
            Assert.True(ControlledSessions.Owns(id));
            Assert.Equal(ControlledActivity.Idle, ControlledSessions.Activity(id));   // seeded idle

            ControlledSessions.SetActivity(id, ControlledActivity.Busy);
            Assert.Equal(ControlledActivity.Busy, ControlledSessions.Activity(id));

            ControlledSessions.Unregister(id);
            Assert.False(ControlledSessions.Owns(id));
            Assert.Null(ControlledSessions.Activity(id));

            // A status update after Unregister must not resurrect the session.
            ControlledSessions.SetActivity(id, ControlledActivity.Busy);
            Assert.Null(ControlledSessions.Activity(id));
        }
        finally { ControlledSessions.Unregister(id); }
    }
}
