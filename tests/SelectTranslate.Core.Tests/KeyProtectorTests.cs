using SelectTranslate.Core.Configuration;
using SelectTranslate.Core.Security;

namespace SelectTranslate.Core.Tests;

public sealed class KeyProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_ReturnsOriginal()
    {
        var encrypted = KeyProtector.Protect("sk-1234567890");

        Assert.NotNull(encrypted);
        Assert.NotEqual("sk-1234567890", encrypted);
        Assert.Equal("sk-1234567890", KeyProtector.Unprotect(encrypted));
    }

    [Fact]
    public void Unprotect_InvalidBase64_ReturnsNull()
    {
        Assert.Null(KeyProtector.Unprotect("not-base64!!!"));
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_ReturnsNull()
    {
        var encrypted = KeyProtector.Protect("sk-abcdef");
        Assert.NotNull(encrypted);

        var bytes = Convert.FromBase64String(encrypted);
        bytes[^1] ^= 0xFF;

        Assert.Null(KeyProtector.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Protect_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(KeyProtector.Protect(null));
        Assert.Null(KeyProtector.Protect(string.Empty));
    }
}

public sealed class ConfigPathsTests
{
    [Fact]
    public void ResolveBaseDirectory_Portable_UsesExeDataFolder()
    {
        var path = ConfigPaths.ResolveBaseDirectory(portable: true, exeDirectory: @"C:\Apps\SelectTranslate");

        Assert.Equal(Path.Combine(@"C:\Apps\SelectTranslate", "data"), path);
    }

    [Fact]
    public void ResolveBaseDirectory_Normal_UsesAppDataSelectTranslate()
    {
        var path = ConfigPaths.ResolveBaseDirectory(portable: false);

        Assert.Equal("SelectTranslate", Path.GetFileName(path));
        Assert.Contains("AppData", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureWritableBase_ReturnsPreferred_WhenDirectoryIsWritable()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        var preferred = Path.Combine(root, "cfg");

        try
        {
            var resolved = ConfigPaths.EnsureWritableBase(preferred, AppContext.BaseDirectory);

            Assert.Equal(Path.GetFullPath(preferred), resolved);
            Assert.True(Directory.Exists(preferred));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EnsureWritableBase_FallsBack_WhenPreferredPathIsBlockedByAFile()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "blocker.txt");
        File.WriteAllText(file, "x");

        try
        {
            var resolved = ConfigPaths.EnsureWritableBase(Path.Combine(file, "cfg"), AppContext.BaseDirectory);

            Assert.NotEqual(Path.GetFullPath(Path.Combine(file, "cfg")), resolved);
            Assert.True(Directory.Exists(resolved));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}