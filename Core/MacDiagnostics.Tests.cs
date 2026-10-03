using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Reading back what macOS will see: the name the app is signed under, and
/// whether its bundle carries a seal.
/// </summary>
/// <remarks>
/// The signature parser is tested against Mach-O files built here rather than
/// against a real one, so the awkward cases — no signature at all, a truncated
/// file, the wrong identifier — can each be produced on purpose. A real signed
/// binary only ever proves the happy path.
/// </remarks>
public class MacDiagnosticsTests : IDisposable
{
    private readonly List<string> _files = new();

    public void Dispose()
    {
        foreach (string f in _files)
        {
            try { File.Delete(f); } catch { }
        }
    }

    private string Temp()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _files.Add(path);
        return path;
    }

    /// <summary>
    /// A 64-bit Mach-O carrying an embedded signature whose CodeDirectory names
    /// <paramref name="identifier"/>. Enough of the format for the parser, which
    /// is all it reads.
    /// </summary>
    private string MachOSignedAs(string? identifier)
    {
        var file = new List<byte>();

        void Little(int v) => file.AddRange(BitConverter.GetBytes(v));

        // Header: magic, cputype, cpusubtype, filetype, ncmds, sizeofcmds,
        // flags, reserved.
        file.AddRange(new byte[] { 0xCF, 0xFA, 0xED, 0xFE });
        Little(0x0100_0007);
        Little(3);
        Little(2);
        Little(identifier == null ? 0 : 1);
        Little(16);
        Little(0);
        Little(0);

        if (identifier == null) return Write(file);

        // One LC_CODE_SIGNATURE pointing past the load commands.
        int signatureAt = 32 + 16;
        Little(0x1D);
        Little(16);
        Little(signatureAt);
        Little(0);

        var signature = new List<byte>();
        void Big(uint v) => signature.AddRange(new[]
        {
            (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v
        });

        byte[] name = Encoding.UTF8.GetBytes(identifier);

        // SuperBlob: magic, length, one entry pointing at the CodeDirectory.
        Big(0xFADE0CC0);
        Big(0);
        Big(1);
        Big(0);   // slot type
        Big(20);  // offset of the CodeDirectory within the signature

        // CodeDirectory: magic, length, version, flags, hashOffset,
        // identOffset, then the name it is signed under.
        Big(0xFADE0C02);
        Big((uint)(24 + name.Length + 1));
        Big(0x20400);
        Big(2);
        Big(0);
        Big(24);  // the identifier sits straight after this header
        signature.AddRange(name);
        signature.Add(0);

        file.AddRange(signature);
        return Write(file);
    }

    private string Write(List<byte> bytes)
    {
        string path = Temp();
        File.WriteAllBytes(path, bytes.ToArray());
        return path;
    }

    // ---- the signing identifier ----

    /// <summary>
    /// The question this exists to answer: the app must be signed as its bundle
    /// identifier, because that is what macOS matches an Accessibility grant
    /// against.
    /// </summary>
    [Fact]
    public void ReadsTheNameABinaryIsSignedUnder()
    {
        string path = MachOSignedAs("com.jinxyjoshua.jinxymac");

        Assert.Equal("com.jinxyjoshua.jinxymac", MacDiagnostics.SigningIdentifier(path));
    }

    /// <summary>
    /// What the .NET SDK leaves behind if the build's re-signing step silently
    /// did not run. Catching that is the point of reading it back.
    /// </summary>
    [Fact]
    public void ReadsTheSdksDefaultNameToo()
    {
        Assert.Equal("apphost", MacDiagnostics.SigningIdentifier(MachOSignedAs("apphost")));
    }

    [Fact]
    public void SaysNothingForABinaryWithNoSignature()
    {
        Assert.Null(MacDiagnostics.SigningIdentifier(MachOSignedAs(null)));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void SurvivesAFileThatIsNotAMachO(byte[] content)
    {
        string path = Temp();
        File.WriteAllBytes(path, content);

        Assert.Null(MacDiagnostics.SigningIdentifier(path));
    }

    [Fact]
    public void SurvivesAFileThatIsNotThere()
    {
        Assert.Null(MacDiagnostics.SigningIdentifier(
            Path.Combine(Path.GetTempPath(), "jinxy-no-such-binary")));
    }

    /// <summary>A truncated download must not take the Settings page down.</summary>
    [Fact]
    public void SurvivesASignatureThatRunsOffTheEndOfTheFile()
    {
        string full = MachOSignedAs("com.jinxyjoshua.jinxymac");
        byte[] bytes = File.ReadAllBytes(full);

        string cut = Temp();
        File.WriteAllBytes(cut, bytes[..(bytes.Length - 12)]);

        Assert.Null(MacDiagnostics.SigningIdentifier(cut));
    }

    // ---- the bundle around it ----

    [Fact]
    public void FindsTheAppBundleAboveTheBinary()
    {
        string bundle = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "JinxyMac.app");
        string macos = Path.Combine(bundle, "Contents", "MacOS");
        Directory.CreateDirectory(macos);

        try
        {
            Assert.Equal(bundle, MacDiagnostics.AppBundlePath(macos));
        }
        finally { Directory.Delete(Path.GetDirectoryName(bundle)!, recursive: true); }
    }

    /// <summary>
    /// Worth reporting rather than hiding: an app run loose from a folder has no
    /// bundle for macOS to hang a permission on.
    /// </summary>
    [Fact]
    public void SaysSoWhenThereIsNoBundle()
    {
        Assert.Null(MacDiagnostics.AppBundlePath(Path.GetTempPath()));
    }

    [Fact]
    public void SeesWhetherTheBundleCarriesASeal()
    {
        string bundle = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "JinxyMac.app");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents"));

        try
        {
            Assert.False(MacDiagnostics.HasBundleSeal(bundle));

            Directory.CreateDirectory(Path.Combine(bundle, "Contents", "_CodeSignature"));
            File.WriteAllText(Path.Combine(bundle, "Contents", "_CodeSignature", "CodeResources"), "");

            Assert.True(MacDiagnostics.HasBundleSeal(bundle));
        }
        finally { Directory.Delete(Path.GetDirectoryName(bundle)!, recursive: true); }
    }

    // ---- the report itself ----

    [Fact]
    public void TheReportCarriesEveryFactWorthAsking()
    {
        string report = MacDiagnostics.Report("1.2.7", "Granted", "Denied");

        foreach (string expected in new[]
        {
            "1.2.7", "Runtime:", "Accessibility: Granted", "Screen recording: Denied",
            "App bundle:", "Running from Applications:", "Signed as:", "Bundle seal:"
        })
        {
            Assert.Contains(expected, report);
        }
    }
}
