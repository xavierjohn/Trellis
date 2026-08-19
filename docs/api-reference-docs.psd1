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
}
