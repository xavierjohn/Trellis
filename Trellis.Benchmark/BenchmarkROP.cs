using Trellis.Primitives;

namespace Benchmark;

using BenchmarkDotNet.Attributes;

using Trellis;
using static Trellis.Result;

/// <summary>
/// Benchmark ROP vs If.
/// 
/// Latest Run (2026-11-12, ShortRun - 3 iterations, .NET 11.0.0, AMD Ryzen 9 9900X, Windows 11, Balanced power plan):
/// | Method        | Mean      | Error     | StdDev    | Gen0   | Allocated |
/// |-------------- |----------:|----------:|----------:|-------:|----------:|
/// | RopStyleHappy |  90.49 ns | 25.816 ns | 1.415 ns  | 0.0167 |     280 B |
/// | IfStyleHappy  |  69.78 ns | 11.771 ns | 0.645 ns  | 0.0086 |     144 B |
/// | RopStyleSad   |  86.54 ns | 24.633 ns | 1.350 ns  | 0.0229 |     384 B |
/// | IfStyleSad    |  59.15 ns |  0.967 ns | 0.053 ns  | 0.0148 |     248 B |
/// | RopSample1    | 308.12 ns | 45.790 ns | 2.510 ns  | 0.0377 |     632 B |
/// | IfSample1     | 213.45 ns | 36.471 ns | 1.999 ns  | 0.0377 |     632 B |
/// 
/// Analysis:
/// - Observed mean gaps: 20.71 ns happy, 27.39 ns sad, 94.67 ns larger pipeline
/// - Basic ROP cases allocate 136 B more; the larger pipeline allocates 632 B in either style
/// - Three-sample ShortRun confidence intervals limit timing conclusions
/// - Full results and isolated BenchmarkDotNet 0.16.0-preview.2 runner details are in BENCHMARKS.md
/// </summary>

public partial class FirstName : RequiredString<FirstName>
{
}

[MemoryDiagnoser]
public class BenchmarkROP
{
    [Benchmark]
    public string RopStyleHappy() =>
        FirstName.TryCreate("Xavier")
            .Combine(EmailAddress.TryCreate("xavier@somewhere.com"))
            .Match(
                onSuccess: ok => ok.Item1 + " " + ok.Item2,
                onFailure: error => error.Detail ?? string.Empty
            );

    [Benchmark]
    public string IfStyleHappy()
    {
        var rFirstName = FirstName.TryCreate("Xavier");
        var rEmailAddress = EmailAddress.TryCreate("xavier@somewhere.com");
        if (rFirstName.TryGetValue(out var firstName) && rEmailAddress.TryGetValue(out var email))
            return firstName + " " + email;

        Error? error = null;
        if (rFirstName.TryGetError(out var fnErr))
            error = fnErr;
        if (rEmailAddress.TryGetError(out var emErr))
            error = error.Combine(emErr);

        return error!.Detail ?? string.Empty;
    }

    [Benchmark]
    public string RopStyleSad() =>
    FirstName.TryCreate("Xavier")
        .Combine(EmailAddress.TryCreate("bad email"))
        .Match(
            onSuccess: ok => ok.Item1 + " " + ok.Item2,
            onFailure: error => error.Detail ?? string.Empty
        );

    [Benchmark]
    public string IfStyleSad()
    {
        var rFirstName = FirstName.TryCreate("Xavier");
        var rEmailAddress = EmailAddress.TryCreate("bad email");
        if (rFirstName.TryGetValue(out var firstName) && rEmailAddress.TryGetValue(out var email))
            return firstName + " " + email;

        Error? error = null;
        if (rFirstName.TryGetError(out var fnErr))
            error = fnErr;
        if (rEmailAddress.TryGetError(out var emErr))
            error = error.Combine(emErr);

        return error!.Detail ?? string.Empty;
    }

    [Benchmark]
    public Result<string> RopSample1()
    {
        var createdAt = DateTime.UtcNow;
        var updatedAt = createdAt.AddMinutes(-10);
        return EmailAddress.TryCreate("xavier@somewhere.com")
            .Combine(FirstName.TryCreate("Xavier"))
            .Combine(LastName.TryCreate(string.Empty))
            .Combine(EmailAddress.TryCreate("xavier @ somewhereelse.com"))
            .Combine(Ensure(createdAt <= updatedAt, new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(updatedAt)), "validation.error") { Detail = "updateAt cannot be less than createdAt" }))))
            .Bind((email, firstName, lastName, anotherEmail, _) => Result.Ok(string.Join(" ", firstName, lastName, email, anotherEmail)));
    }

    [Benchmark]
    public Result<string> IfSample1()
    {
        var createdAt = DateTime.UtcNow;
        var updatedAt = createdAt.AddMinutes(-10);
        var hrEmail = EmailAddress.TryCreate("xavier@somewhere.com");
        var hrFname = FirstName.TryCreate("Xavier");
        var hrLname = LastName.TryCreate(string.Empty);
        var hrEmailSec = EmailAddress.TryCreate("xavier @ somewhereelse.com");
        var hrDateCheck = Ensure(createdAt <= updatedAt, new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(updatedAt)), "validation.error") { Detail = "updateAt cannot be less than createdAt" })));

        Error? error = null;
        if (hrEmail.TryGetError(out var emErr))
            error = emErr;
        if (hrFname.TryGetError(out var fnErr))
            error = error.Combine(fnErr);
        if (hrLname.TryGetError(out var lnErr))
            error = error.Combine(lnErr);
        if (hrEmailSec.TryGetError(out var em2Err))
            error = error.Combine(em2Err);
        if (hrDateCheck.TryGetError(out var dateErr))
            error = error.Combine(dateErr);

        if (error == null
            && hrEmail.TryGetValue(out var email)
            && hrFname.TryGetValue(out var firstName)
            && hrLname.TryGetValue(out var lastName)
            && hrEmailSec.TryGetValue(out var anotherEmail))
        {
            return Result.Ok(string.Join(" ", firstName, lastName, email, anotherEmail));
        }

        return Result.Fail<string>(error!);
    }
}