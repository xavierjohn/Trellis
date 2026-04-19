using Trellis.Primitives;

namespace Benchmark;

using BenchmarkDotNet.Attributes;
using SampleUserLibrary;
using Trellis;
using static Trellis.Result;

/// <summary>
/// Benchmark ROP vs If.
/// 
/// Latest Run (ShortRun - 3 iterations, .NET 10.0.1, Intel Core i7-1185G7 @ 3.00GHz, Windows 11):
/// | Method        | Mean      | Error     | StdDev    | Gen0   | Allocated |
/// |-------------- |----------:|----------:|----------:|-------:|----------:|
/// | RopStyleHappy | 146.89 ns | 24.45 ns  | 1.340 ns  | 0.0229 |     144 B |
/// | IfStyleHappy  | 131.27 ns | 30.31 ns  | 1.662 ns  | 0.0229 |     144 B |
/// | RopStyleSad   |  99.16 ns | 63.06 ns  | 3.457 ns  | 0.0293 |     184 B |
/// | IfStyleSad    |  87.60 ns | 57.17 ns  | 3.134 ns  | 0.0293 |     184 B |
/// 
/// Analysis:
/// - ROP adds ~16 ns overhead on happy path (~12% slower than imperative)
/// - ROP adds ~11 ns overhead on sad path (~13% slower than imperative)
/// - Memory allocations are identical between ROP and imperative styles
/// - The overhead is negligible compared to typical I/O operations (database, HTTP, etc.)
/// 
/// Previous Runs (for reference):
/// Run 1
/// | Method        | Mean      | Error    | StdDev   | Gen0   | Allocated |
/// |-------------- |----------:|---------:|---------:|-------:|----------:|
/// | RopStyleHappy | 147.62 ns | 1.529 ns | 1.430 ns | 0.0229 |     144 B |
/// | IfStyleHappy  | 126.27 ns | 0.916 ns | 0.812 ns | 0.0229 |     144 B |
/// | RopStyleSad   |  77.76 ns | 1.169 ns | 1.093 ns | 0.0331 |     208 B |
/// | IfStyleSad    |  72.32 ns | 1.400 ns | 1.309 ns | 0.0331 |     208 B |
/// 
/// Run 2
/// | Method        | Mean      | Error    | StdDev   | Gen0   | Allocated |
/// |-------------- |----------:|---------:|---------:|-------:|----------:|
/// | RopStyleHappy | 145.82 ns | 2.058 ns | 1.825 ns | 0.0229 |     144 B |
/// | IfStyleHappy  | 123.99 ns | 2.309 ns | 2.047 ns | 0.0229 |     144 B |
/// | RopStyleSad   |  79.50 ns | 1.395 ns | 1.305 ns | 0.0331 |     208 B |
/// | IfStyleSad    |  73.71 ns | 1.151 ns | 0.961 ns | 0.0331 |     208 B |
/// </summary>

public partial class FirstName : RequiredString<FirstName>
{
}

#pragma warning disable CA1822 // Mark members as static

[MemoryDiagnoser]
public class BenchmarkROP
{
    [Benchmark]
    public string RopStyleHappy() =>
        FirstName.TryCreate("Xavier")
            .Combine(EmailAddress.TryCreate("xavier@somewhere.com"))
            .Match(
                onSuccess: ok => ok.Item1 + " " + ok.Item2,
                onFailure: error => error.Detail
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

        return error!.Detail;
    }

    [Benchmark]
    public string RopStyleSad() =>
    FirstName.TryCreate("Xavier")
        .Combine(EmailAddress.TryCreate("bad email"))
        .Match(
            onSuccess: ok => ok.Item1 + " " + ok.Item2,
            onFailure: error => error.Detail
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

        return error!.Detail;
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
            .Combine(Ensure(createdAt <= updatedAt, Error.Validation("updateAt cannot be less than createdAt", nameof(updatedAt))))
            .Bind((email, firstName, lastName, anotherEmail) => Result.Ok(string.Join(" ", firstName, lastName, email, anotherEmail)));
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
        var hrDateCheck = Ensure(createdAt <= updatedAt, Error.Validation("updateAt cannot be less than createdAt", nameof(updatedAt)));

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