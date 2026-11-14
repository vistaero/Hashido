using System.Security.AccessControl;
using System.Security.Principal;
using Apfs.IO;
using Apfs.Mount;
using Apfs.Write;

namespace Apfs.Tests;

public class WritableAclTests(LinuxFixtures fixtures) : IClassFixture<LinuxFixtures>
{
    [Fact]
    public void WritableVolumeAclExcludesEveryoneAndAllowsServiceUsers()
    {
        var (path, grant) = FsOpsTests.Prepare(fixtures, "mk-ci-64m");
        using var device = new FileImageWriter(path, grant);
        using var session = WriteSession.Open(device, grant);
        using var fs = new ApfsWritableFileSystem(session, 0, TimeSpan.FromHours(1));

        byte[] descriptor = [];
        Assert.Equal(ApfsFileSystem.StatusSuccess, fs.GetSecurityByName("\\", out _, ref descriptor));
        var security = new RawSecurityDescriptor(descriptor, 0);
        var allowed = security.DiscretionaryAcl!.Cast<CommonAce>()
            .Where(ace => ace.AceQualifier == AceQualifier.AccessAllowed)
            .Select(ace => ace.SecurityIdentifier)
            .ToArray();

        Assert.Contains(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), allowed);
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), allowed);
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), allowed);
        Assert.DoesNotContain(new SecurityIdentifier(WellKnownSidType.WorldSid, null), allowed);
    }
}
