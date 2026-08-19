@{
    # Reference docs that describe the framework as a whole rather than one package, so no
    # <TrellisApiRefName> owns them. Two consumers rely on this list and would silently
    # disagree if each kept its own copy:
    #
    #   docs/lint-api-reference.ps1   TRLDOC004 - a doc that is neither owned nor listed here
    #                                 has no source directory to be checked against.
    #   docs/audit-doc-freshness.ps1  compares these against every package's source, since any
    #                                 package's change can invalidate them.
    CrossCuttingDocs = @(
        'trellis-api-cookbook.md',
        'trellis-api-anti-patterns.md',
        'trellis-value-object-taxonomy.md',
        'trellis-start-here.md'
    )

    # Generated or repo-internal reports that live alongside the references but are never
    # shipped to consumers and have no source to be verified against.
    UnshippedDocs = @(
        'completeness-report.md'
    )

    # Docs that describe GUARDRAILS rather than APIs, and so must carry the banner below.
    #
    # Every other reference is self-correcting: describe an API the consumer does not have
    # and they get a compile error - loud, immediate, cheap. Analyzer docs have no such net.
    # Trellis.Analyzers is opt-in, and Trellis.Core now delivers these files to every
    # consumer, so an agent can read a TRLS rule in a project where nothing enforces it and
    # then write LESS defensively - trusting, say, TRLS003 to catch an unsafe Maybe.Value
    # that will now ship. The failure is silent, permanent and the opposite of what the doc
    # intended, which is what earns these two files an exception.
    GuardrailDocs = @(
        'trellis-api-analyzers.md',
        'trellis-api-anti-patterns.md'
    )

    # Verbatim opening of the required banner. TRLDOC012 matches this prefix, so treat it as
    # a contract: reword the sentence in the docs and the gate must be updated with it.
    GuardrailBannerMarker = '> **Requires `Trellis.Analyzers`.**'
}
