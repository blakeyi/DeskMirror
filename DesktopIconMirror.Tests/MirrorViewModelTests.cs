using DesktopIconMirror.Models;
using DesktopIconMirror.ViewModels;
using Xunit;

namespace DesktopIconMirror.Tests;

public class MirrorViewModelTests
{
    [Fact]
    public void BuildShowPropertiesStartInfo_UsesPropertiesVerb()
    {
        var psi = MirrorViewModel.BuildShowPropertiesStartInfo(@"C:\Users\me\Desktop\app.lnk");

        Assert.Equal(@"C:\Users\me\Desktop\app.lnk", psi.FileName);
        Assert.Equal("properties", psi.Verb);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void BuildRunAsAdminStartInfo_UsesRunAsVerb()
    {
        var psi = MirrorViewModel.BuildRunAsAdminStartInfo(@"C:\Users\me\Desktop\app.lnk");

        Assert.Equal(@"C:\Users\me\Desktop\app.lnk", psi.FileName);
        Assert.Equal("runas", psi.Verb);
        Assert.True(psi.UseShellExecute);
    }

    [Theory]
    [InlineData(@"shell:MyComputerFolder", true, false)]
    [InlineData(@"C:\Users\me\Desktop\folder", true, false)]
    [InlineData(@"C:\Users\me\Desktop\app.exe", false, true)]
    [InlineData(@"C:\Users\me\Desktop\app.lnk", false, true)]
    public void CanRunAsAdministrator_FiltersUnsupportedTargets(string targetPath, bool isFolder, bool expected)
    {
        var icon = new DesktopIcon
        {
            TargetPath = targetPath,
            IsFolder = isFolder
        };

        Assert.Equal(expected, MirrorViewModel.CanRunAsAdministrator(icon));
    }
}
