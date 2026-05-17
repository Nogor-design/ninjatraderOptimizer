# Optimizer Project RAG Workflow

## Goal

Use local retrieval and a compile-feedback loop to develop NinjaTrader optimizer and optimization fitness code without guessing at NinjaTrader APIs.

This is a sidecar workflow. Nothing in this RAG system is compiled into `NinjaTraderOptimizerProject.dll`.

## Existing Documentation RAG Source

The local NinjaTrader documentation RAG project lives at:

```text
D:\Backup\projects\PythonProject\NinjatraderDocScrapper
```

Important files:

- `LLM_DOCUMENTATION_GUIDE.md`
- `README.md`
- `generate_ninjascript.py`
- `build_ollama_index.py`
- `ninjatrader_docs\rag_index.sqlite`

For optimizer work, prioritize retrieved documentation for:

- References Optimizer
- References Optimization Fitness
- Strategy Analyzer optimization behavior
- NinjaScript lifecycle behavior

If retrieved docs do not prove an API, do not guess the signature. Inspect NinjaTrader assemblies or ask for a focused documentation retrieval step.

## Local Memory In This Project

This project keeps its own learning notes under:

```text
rag\
```

Tracked files:

- `rag\learning_log.md`
- `rag\prompts\optimizer_repair_prompt.md`

Ignored generated files:

- `rag\build_logs\`
- `rag\outputs\`

The learning log should record facts we verify by build or NinjaTrader runtime testing. It should not become a dumping ground for guesses.

## Recommended Development Loop

1. Pick one narrow optimizer or fitness change.
2. Query docs before touching unfamiliar NinjaTrader APIs.
3. Make the smallest code change that tests the API assumption.
4. Build with:

   ```powershell
   .\tools\Build-WithLearningLog.ps1
   ```

5. If the build fails, save the generated log path and use:

   ```powershell
   .\tools\Ask-NinjaTraderRag.ps1 `
     -Task "Fix the NinjaTrader optimizer project compile errors while preserving behavior." `
     -ExistingCodeFile .\NinjaTraderOptimizerProject\Optimizers\CustomMultiObjectiveOptimizer.cs `
     -CompilerErrorsFile .\rag\build_logs\<latest-log>.txt
   ```

6. Apply the fix.
7. Rebuild.
8. When a fact is verified, add a short note to `rag\learning_log.md`.
9. After NinjaTrader runtime testing, record selector/runtime behavior in `PROJECT_STATUS.md`.

## What To Record

Good learning-log entries answer:

- What did we try?
- What did NinjaTrader or MSBuild report?
- What fixed it?
- Which file and line changed?
- Was it verified by MSBuild, NinjaTrader runtime, or both?

Example:

```text
## 2026-05-11 - Optimizer parameter API

Question:
- Can optimizer code set Parameter.Value directly inside OnOptimize?

Evidence:
- MSBuild result: success/failure log path.
- NinjaTrader runtime result: appeared/did not appear in Strategy Analyzer.

Decision:
- Use/avoid the API.

Follow-up:
- Query docs for result collection after WaitForIterationsCompleted().
```

## Prompt Rules For Future Chats

When continuing this project in a new chat, tell the assistant:

- Read `PROJECT_PLAN.md`, `PROJECT_STATUS.md`, and `RAG_WORKFLOW.md`.
- Use `D:\Backup\projects\PythonProject\NinjatraderDocScrapper\LLM_DOCUMENTATION_GUIDE.md` for NinjaTrader coding rules.
- Use the local RAG docs before changing unfamiliar optimizer APIs.
- Build with MSBuild, not `dotnet build`.
- Keep optimizer/fitness code out of the batch AddOn project.
