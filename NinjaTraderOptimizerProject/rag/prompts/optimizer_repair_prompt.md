# Optimizer Repair Prompt

Use this prompt when compiler or NinjaTrader runtime errors appear while developing the standalone optimizer project.

```text
We are developing a standalone NinjaTrader 8 optimizer/fitness DLL.

Project:
D:\ninjatraderOptimizer\NinjaTraderOptimizerProject

Read first:
- PROJECT_PLAN.md
- PROJECT_STATUS.md
- RAG_WORKFLOW.md
- D:\Backup\projects\PythonProject\NinjatraderDocScrapper\LLM_DOCUMENTATION_GUIDE.md

Rules:
- Build with MSBuild, not dotnet build.
- Use retrieved NinjaTrader documentation before changing unfamiliar optimizer or optimization fitness APIs.
- Prioritize docs for References Optimizer and References Optimization Fitness.
- Do not invent NinjaTrader methods, properties, namespaces, or enum values.
- If docs do not prove an API exists, say what evidence is missing.
- Keep optimizer/fitness code in this standalone project, not the batch AddOn project.
- Fix compiler errors directly while preserving intended behavior.

Task:
Fix the current optimizer/fitness issue using the provided code, retrieved docs, and compiler/runtime errors. After fixing, update rag\learning_log.md with the verified fact.
```
