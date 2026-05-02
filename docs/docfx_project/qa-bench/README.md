# Trellis docfx Q&A bench

A static, scriptable LLM bench that A/B-tests changes to the docfx
documentation. Runs a fixed question bank against a hosted chat model, scores
answers against verifier rules, and writes a markdown report.

Goal: produce a quantitative signal — *did this doc PR actually help an LLM
answer questions?* — before merging structural changes (e.g., splitting an
oversized doc into multiple files).

## Baseline (frozen reference)

`BASELINE.md` records the most recent committed scorecard against the
unmodified docfx doc set. As of the initial commit:

- Model: `gpt-4.1-2025-04-14` on Azure AI Foundry
- Question bank: 23 questions, 5 categories (core, cookbook, asp, efcore, control)
- **Score: 22 / 23 (95.7%)**, 3-iteration determinism confirmed
- Single failure: `core-traverse-overloads` (model miscounts overloads documented in a long table — a candidate signal for whether splitting `trellis-api-core.md` improves needle-in-haystack retrieval)

A doc PR is judged "doc-quality positive" when its post-change score *strictly
improves* on a target category and does not regress any control category.

## What it is, what it isn't

- **It is** a small, hand-curated bench (23 questions) where every ground
  truth is derived directly from `Trellis.*/src/**.cs`.
- **It is** a one-shot batched run: the entire docfx doc set (articles +
  api_reference) is concatenated into a single prompt, so the model has to
  *navigate* it the same way an unprimed LLM session would.
- **It is not** a substitute for the full `Trellis-training/` lab (83-criterion
  rubric, full code-generation evaluation). Use the lab for whole-framework
  comprehension. Use this bench for fast doc-change A/B.
- **It is not** wired into CI today. Designed to be runnable manually before
  and after a doc PR.

## Layout

| File | Purpose |
|---|---|
| `questions.json` | Question bank with verifier rules (`all_of` / `any_of` / `none_of`) |
| `run-qa-bench.ps1` | Runner: builds prompt, calls Azure AI Foundry, saves result |
| `score-qa-bench.ps1` | Scorer: applies verifiers, writes `.score.json` + `.score.md` |
| `BASELINE.md` | Frozen baseline scorecard. Update only when (a) a doc PR demonstrably changes the score, or (b) the question bank itself changes. |
| `results/` | Run outputs (gitignored) |

## Prerequisites

1. PowerShell 7+
2. An Azure AI Foundry deployment of a chat model with **≥256K input context**
   (the doc set is ~250K tokens). GPT-4.1 is the default; o3-mini, Claude
   Sonnet 4 also work.
3. Two env vars:
   - `AZURE_AI_ENDPOINT` — e.g., `https://yourname.services.ai.azure.com`
   - `AZURE_AI_KEY`      — the api-key (do **not** commit)

Optional:
- `AZURE_AI_MODEL` — defaults to `gpt-4.1`

## Run

```pwsh
$env:AZURE_AI_ENDPOINT = 'https://yourname.services.ai.azure.com'
$env:AZURE_AI_KEY      = '...'

# baseline run, scored
pwsh ./run-qa-bench.ps1 -Tag baseline -Score

# 5 iterations for variance estimation
pwsh ./run-qa-bench.ps1 -Tag baseline -Iterations 5 -Score

# preview the prompt without calling the API (free)
pwsh ./run-qa-bench.ps1 -Tag dryrun -DryRun
```

## A/B workflow

1. **Before** a doc PR: `pwsh ./run-qa-bench.ps1 -Tag baseline -Iterations 3 -Score`
2. Apply the doc change on a branch.
3. **After**: `pwsh ./run-qa-bench.ps1 -Tag <branch-name> -Iterations 3 -Score`
4. Compare `.score.md` files. Ship the change only if:
   - **target-category** scores improve, AND
   - **control-category** scores don't regress.

## Cost estimate

GPT-4.1 at ~$2/M input + $8/M output, ~250K input tokens × 1 call/run:
- Single run: **≈ $0.50**
- 3-iteration baseline + 3-iteration post-change: **≈ $3**

## Adding questions

Each entry in `questions.json` needs:

```jsonc
{
  "id": "unique-stable-id",
  "category": "core | cookbook | asp | efcore | control",
  "target_doc": "trellis-api-core.md",
  "difficulty": "easy | medium | hard",
  "question": "...",
  "verifier": {
    "all_of":  ["string1", "string2"],          // AND: every keyword must appear (case-insensitive substring)
    "any_of":  [["alt1a","alt1b"], ["alt2a"]],  // groups: at least one keyword from each group must appear
    "none_of": ["antipattern1"]                  // NONE may appear (e.g., v1 anti-patterns)
  },
  "source_ref": "Trellis.X/src/Y.cs:123 + brief justification"
}
```

Rules of thumb:
- **Ground truth must come from source**, not from another doc. Cite
  `source_ref` precisely.
- Prefer questions where the answer is a single token / signature / type name.
- Use `none_of` to catch v1 anti-patterns (`Error.Validation(`, `Page.Create(`,
  etc.) the LLM might still suggest.
- Mix categories so that *control* questions (about un-changed docs) don't
  vary across runs — they're your noise floor.

## Limitations

- Verifiers use case-insensitive substring matching, not semantic equivalence.
  Phrase questions to constrain answer shape ("reply with the type signature
  only") to keep matching reliable.
- Single model, single run = noisy. Use `-Iterations 3` minimum for any
  judgment.
- The bench measures *retrieval-from-context*, not free-recall. It can't tell
  you the docs are wrong, only that they're hard or easy to navigate.
