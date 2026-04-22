namespace Trellis;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Provides an expanded debugger view for <see cref="Result{TValue}"/> that shows
/// only the relevant properties (Value for success, Error for failure).
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ResultDebugView<TValue>
{
    private readonly Result<TValue> _result;

    public ResultDebugView(Result<TValue> result) => _result = result;

    public bool IsSuccess => _result.IsSuccess;

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public object? Details => _result.IsSuccess
        ? new SuccessView(_result)
        : new FailureView(_result);

    [DebuggerDisplay("Value = {Value}")]
    internal sealed class SuccessView
    {
        private readonly Result<TValue> _result;

        public SuccessView(Result<TValue> result) => _result = result;

        public TValue? Value => _result.TryGetValue(out var v) ? v : default;
    }

    [DebuggerDisplay("Error = {Error}")]
    internal sealed class FailureView
    {
        private readonly Result<TValue> _result;

        public FailureView(Result<TValue> result) => _result = result;

        public Error? Error => _result.Error;

        public string? Code => _result.Error?.Code;

        public string? Detail => _result.Error?.Detail;

        public string? Kind => _result.Error?.Kind;

        public string? ErrorType => _result.Error?.GetType().Name;
    }
}