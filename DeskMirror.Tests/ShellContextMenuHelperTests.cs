using DeskMirror.Shell;
using Xunit;

namespace DeskMirror.Tests;

public class ShellContextMenuHelperTests
{
    [Fact]
    public void CreateInvokeCommandRequest_UsesUnicodeAndClickPointFlags()
    {
        var request = ShellContextMenuHelper.CreateInvokeCommandRequest((nint)0x1234, 37, 200, 300);

        Assert.Equal((nint)0x1234, request.Hwnd);
        Assert.Equal(37, request.CommandOffset);
        Assert.Equal(200, request.ScreenX);
        Assert.Equal(300, request.ScreenY);
        Assert.Equal(0x20004000u, request.Flags);
    }

    [Theory]
    [InlineData("打开(&O)", true)]
    [InlineData("Open", true)]
    [InlineData("&Open", true)]
    [InlineData("Open\tEnter", true)]
    [InlineData("属性(&R)", false)]
    [InlineData("Delete", false)]
    public void IsOpenMenuText_DetectsOpenCommands(string menuText, bool expected)
    {
        Assert.Equal(expected, ShellContextMenuHelper.IsOpenMenuText(menuText));
    }

    [Theory]
    [InlineData("shell:MyComputerFolder", false)]
    [InlineData("::{20D04FE0-3AEA-1069-A2D7-08002B30309D}", false)]
    [InlineData("C:\\Users\\Public\\Desktop\\微信.lnk", true)]
    [InlineData("C:\\Users\\yihonggen\\Desktop\\Test Folder", true)]
    public void SupportsNativeContextMenu_FiltersVirtualShellItems(string parsingName, bool expected)
    {
        Assert.Equal(expected, ShellContextMenuHelper.SupportsNativeContextMenu(parsingName));
    }
}
