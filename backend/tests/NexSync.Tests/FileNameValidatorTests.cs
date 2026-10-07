using NexSync.Application.Validators;
using NexSync.Domain.Exceptions;

namespace NexSync.Tests;

[TestClass]
public sealed class FileNameValidatorTests
{
    [TestMethod]
    [DataRow("report.pdf")]
    [DataRow("My Folder")]
    [DataRow("home.dart")]
    [DataRow("tugas_final.docx")]
    public void Validate_AcceptsValidNames(string name)
    {
        FileNameValidator.Validate(name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("test?.txt")]
    [DataRow("a/b.txt")]
    [DataRow("a\\b.txt")]
    [DataRow("a:b.txt")]
    [DataRow("a*b.txt")]
    [DataRow("a\"b.txt")]
    [DataRow("a<b.txt")]
    [DataRow("a>b.txt")]
    [DataRow("a|b.txt")]
    [DataRow("trailingdot.")]
    [DataRow("trailingspace ")]
    [DataRow("CON")]
    [DataRow("con.txt")]
    [DataRow("PRN")]
    [DataRow("AUX.doc")]
    [DataRow("NUL")]
    [DataRow("COM1")]
    [DataRow("LPT1.txt")]
    public void Validate_RejectsInvalidNames(string name)
    {
        Assert.ThrowsExactly<FileNameInvalidException>(() => FileNameValidator.Validate(name));
    }

    [TestMethod]
    public void Validate_RejectsTooLongName()
    {
        Assert.ThrowsExactly<FileNameInvalidException>(() => FileNameValidator.Validate(new string('a', 256)));
    }
}



