namespace Trellis;

using System.Collections.Immutable;
using System.Text;

internal struct ErrorAccumulator
{
    private Error? _first;
    private bool _mergingValidation;
    private ImmutableArray<FieldViolation>.Builder? _fields;
    private ImmutableArray<RuleViolation>.Builder? _rules;
    private ImmutableArray<Error>.Builder? _errors;
    private string? _detail;
    private StringBuilder? _details;
    private Error? _cause;

    internal void Add(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (_first is null)
        {
            _first = error;
            return;
        }

        if (_errors is not null)
        {
            AppendError(error);
            return;
        }

        if (_first is Error.InvalidInput first && error is Error.InvalidInput next)
        {
            if (!_mergingValidation)
            {
                _mergingValidation = true;
                AppendValidation(first);
            }

            AppendValidation(next);
            return;
        }

        _errors = ImmutableArray.CreateBuilder<Error>();
        AppendError(BuildValidation());
        AppendError(error);
    }

    internal readonly Error? Build() => _errors is not null
        ? new Error.Aggregate(new EquatableArray<Error>(_errors.ToImmutable()))
        : _first is null ? null : BuildValidation();

    private readonly Error BuildValidation() => !_mergingValidation
        ? _first!
        : new Error.InvalidInput(
            new EquatableArray<FieldViolation>(_fields?.ToImmutable() ?? ImmutableArray<FieldViolation>.Empty),
            new EquatableArray<RuleViolation>(_rules?.ToImmutable() ?? ImmutableArray<RuleViolation>.Empty))
        {
            Detail = _details?.ToString() ?? _detail,
            Cause = _cause
        };

    private void AppendError(Error error)
    {
        if (error is Error.Aggregate aggregate)
            _errors!.AddRange(aggregate.Errors.Items);
        else
            _errors!.Add(error);
    }

    private void AppendValidation(Error.InvalidInput error)
    {
        if (!error.Fields.IsEmpty)
        {
            _fields ??= ImmutableArray.CreateBuilder<FieldViolation>(error.Fields.Length);
            _fields.AddRange(error.Fields.Items);
        }

        if (!error.Rules.IsEmpty)
        {
            _rules ??= ImmutableArray.CreateBuilder<RuleViolation>(error.Rules.Length);
            _rules.AddRange(error.Rules.Items);
        }

        _cause ??= error.Cause;
        AppendDetail(error.Detail);
    }

    private void AppendDetail(string? detail)
    {
        if (detail is null)
            return;

        if (_details is not null)
        {
            if (!_details.Equals(detail.AsSpan()))
                _details.Append("; ").Append(detail);
        }
        else if (_detail is null)
        {
            _detail = detail;
        }
        else if (!string.Equals(_detail, detail, StringComparison.Ordinal))
        {
            _details = new StringBuilder(_detail).Append("; ").Append(detail);
        }
    }
}