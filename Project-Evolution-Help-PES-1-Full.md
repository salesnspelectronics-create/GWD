# Project Evolution Help — PES-1

## 1. What PES-1 is

**PES-1** means **Project Evolution Standard — Version 1**.

PES-1 is a reusable standard for maintaining a living, machine-readable project record alongside software Source. Its goal is to make project continuation portable and reliable.

For a project that adopts PES-1, the intended normal continuation package is:

1. the latest complete **Source**; and
2. the latest **Project-Evolution-V<version>.json**.

A separate Handoff document is not required for normal continuation. Durable project knowledge belongs in Project Evolution itself.

PES-1 is currently operationalized for **software projects**. Its section names are intentionally general enough to permit future profiles, but non-software profiles are not defined in PES-1.

---

## 2. What Project Evolution is

Project Evolution is not just a changelog, requirements list, README, handoff, or architecture document.

It combines the durable parts of all of those into one traceable project record:

- project identity and purpose;
- current implementation/runtime state;
- technology and architecture;
- source structure;
- domain/business model;
- requirements and business rules;
- architecture/product decisions and their rationale;
- constraints and safety rules;
- runtime defects/findings;
- build/test/deploy/runtime evidence;
- module status;
- execution queue;
- project risks and unresolved questions;
- release/evolution history;
- development governance;
- onboarding instructions for a new human or AI developer.

The Source remains the evidence for what is actually implemented. Project Evolution records intent, decisions, state, evidence, and project history. Runtime claims require runtime evidence.

---

## 3. Three independent version concepts

PES-1 distinguishes three versions:

### Software Version
The actual source/executable release, for example `V56`.

### Project Evolution Version
The Project Evolution state aligned with a development cycle, for example `Project-Evolution-V56.json`.

### PES Version
The reusable standard/schema version, for example `PES-1`.

The PES version does **not** increase every time the software version changes. It changes only when the standard itself materially changes.

---

## 4. Bootstrap: adopting PES-1 in an existing project that never had Project Evolution

Use `Project-Evolution-V1.json` as a **bootstrap template**.

The template contains no project-specific facts. `UNSET`, empty arrays and empty objects are intentional.

### Required bootstrap procedure

When a fresh development chat receives:
- an existing software Source;
- `Project-Evolution-V1.json`;
- this Help file;

it should do the following before normal development:

1. **Inspect the actual Source.**
2. Identify the actual current Source/release version if it can be proven.
3. Inventory the repository/source structure.
4. Identify frameworks, languages, runtime, package/build files and deployment assets.
5. Identify the database/data-access model, migrations/initializers and persistence rules.
6. Identify authentication/authorization, permissions, logging/audit, file storage and major integrations.
7. Identify major modules and cross-cutting capabilities.
8. Identify build, test and publish commands/processes.
9. Populate `current_state` only from evidence.
10. Populate `system_map`, `architecture` and `source_structure`.
11. Populate `module_state`.
12. Record known defects, blockers, risks and unknowns.
13. Record any existing README, old handoff, reports, notes or historical artifacts as evidence/history.
14. **Do not invent missing history.**
15. Return an initialized `Project-Evolution-V1.json` before, or together with, the first development release.

If the Source is already version V56, V100, etc., the bootstrap PE can still start as **Project Evolution V1** if the user wants the Project Evolution lifecycle to begin now. The software version and PE version are separate fields. After the first normal development cycle, the project may adopt an aligned release policy if desired.

---

## 5. Source + Project Evolution operating model

After bootstrap, the normal development input should be:

- latest Source;
- latest Project Evolution.

The development chat should not require a separate Handoff to understand:
- what the project is;
- how it is built;
- how it is structured;
- what was decided;
- what must not be changed;
- what is implemented;
- what still needs runtime verification;
- what should be done next.

Temporary reports may still be produced for convenience, audits or communication, but durable information must be merged back into Project Evolution.

---

## 6. Recommended read order

A new developer or AI should read the Project Evolution file in this order:

1. `_pes`
2. `project_identity`
3. `purpose_and_scope`
4. `current_state`
5. `system_map`
6. `architecture`
7. `source_structure`
8. `module_state`
9. `execution_queue`
10. `governance`
11. relevant `requirements`, `decisions`, `runtime_findings`
12. `evolution_history` only when historical traceability is needed

This avoids treating an old historical requirement as a current instruction.

---

## 7. Section reference

### `_pes`
Identifies the PES standard/profile, PE/software version and bootstrap state.

Do not place detailed business logic here.

### `project_identity`
Basic identity of the project:
- project name;
- project code;
- owner;
- source/repository name;
- current Source release;
- current Project Evolution release.

### `purpose_and_scope`
Explains why the software exists and what is in/out of scope.

### `current_state`
The fastest current-state snapshot.

It should answer:
- What release/source is current?
- Does it build?
- Has it been deployed?
- Has the database been checked?
- What runtime verification is pending?
- What defects/blockers are currently active?

`current_state` must not make claims that only historical records support.

### `system_map`
High-level map of:
- modules;
- cross-cutting capabilities;
- integrations;
- data flows;
- external dependencies.

It is an orientation layer, not a substitute for Source inspection.

### `architecture`
Durable technical architecture:
- technology stack;
- backend/frontend;
- database;
- deployment;
- auth/permissions;
- logging/audit;
- file storage;
- background jobs;
- integrations;
- architecture constraints;
- architecture unknowns.

### `source_structure`
Repository/source orientation:
- top-level folders/projects;
- major packages;
- entry points;
- build/test scripts;
- deployment assets;
- generated/ignored paths.

This section should help a new developer quickly find the right code.

### `domain_model`
Business/domain concepts:
- entities;
- value objects;
- aggregates;
- workflows/business processes;
- business rules;
- domain unknowns.

Do not force software architecture concepts into domain concepts.

### `requirements`
Material user/product requirements.

Each important requirement should preserve:
- near-literal `user_statement`;
- separate interpretation;
- module/priority;
- maturity;
- implementation status;
- runtime status;
- target version;
- acceptance criteria;
- traceability relationships;
- evidence.

### `decisions`
Durable product/architecture decisions.

A good decision record contains:
- what was decided;
- why;
- alternatives considered;
- consequences;
- status;
- relationships to older decisions.

This is ADR-like behavior inside Project Evolution.

### `runtime_findings`
Real runtime defects or observations.

Record:
- symptom;
- expected behavior;
- actual behavior;
- environment;
- severity;
- root cause if known;
- fix version;
- runtime retest state;
- evidence.

Do not mark a runtime defect fixed merely because code changed.

### `constraints`
Technical, business, security, performance, compatibility, regulatory and operational constraints.

### `risks_and_unknowns`
Unresolved risk and uncertainty.

Unknowns are never silently deleted. When resolved, preserve the original unknown and link its resolution to the final requirement/decision.

### `verification_and_evidence`
Evidence grouped by level:
- Source inspection;
- build;
- automated test;
- runtime;
- database;
- deployment.

Keep those evidence levels distinct.

### `module_state`
Current state by module.

Typical fields may include:
- module name;
- maturity;
- implementation state;
- runtime state;
- known defects;
- dependencies;
- next action.

### `execution_queue`
The currently authorized or analyzed next work.

It should clearly distinguish:
- analysis only;
- approved;
- ready for execution;
- blocked;
- runtime verification.

### `evolution_history`
Append-oriented release/project history.

History is never silently rewritten to make the current project look cleaner.

### `governance`
The rules for maintaining Project Evolution and developing the project.

### `status_model`
Defines implementation status and maturity semantics.

### `record_templates`
Reusable record shapes for requirements, decisions, runtime findings and unknowns.

### `onboarding`
Instructions for a new developer/AI to enter the project safely.

---

## 8. Requirements capture

A material user requirement should preserve the user's intent as faithfully as practical.

Do not collapse these into one field:
- what the user said;
- what the analyst understood;
- what was approved;
- what was implemented;
- what was runtime-verified.

Example lifecycle:

`Dialogue → Analyzed → Approved → Ready for Execution → In Implementation → Runtime Verification → Closed`

This **maturity** dimension is separate from implementation status.

Typical implementation statuses:

- `captured`
- `analysis`
- `approved`
- `in_progress`
- `implemented_pending_runtime_verification`
- `blocked`
- `deferred`
- `superseded`
- `done`

---

## 9. Traceability

PES-1 is append-oriented.

Do not erase an old record merely because it is no longer current.

Use explicit relationships:

- `supersedes` — replaces an earlier requirement/decision.
- `narrows` — clarifies or restricts an earlier item without erasing it.
- `depends_on` — depends on another item.
- `blocks` — prevents another item from safely proceeding.
- `related_to` — related but not controlling.
- `changed_during_implementation` — requirement changed while development was already underway.

The current rule should be discoverable without destroying the historical chain.

---

## 10. Decisions

Important architecture/product decisions should preserve rationale.

A future developer should be able to answer:
- What was decided?
- Why?
- What alternatives were considered?
- What trade-offs/consequences were accepted?
- Has this decision been superseded?

Do not store only the final choice if the reason matters to future development.

---

## 11. Current state vs history

This separation is critical.

### Current State
What is true now.

### History
What happened before.

A developer should not need to scan 200 historical records merely to know the current release state.

At the same time, history must remain available for traceability.

---

## 12. Evidence hierarchy

Use evidence appropriate to the claim.

A safe practical hierarchy is:

1. **Explicit user runtime confirmation** for user acceptance/Done.
2. **Runtime/deployed evidence** for runtime behavior.
3. **Database/runtime inspection** for stored-data claims.
4. **Build/test evidence** for build/test claims.
5. **Actual Source inspection** for implementation claims.
6. **Project Evolution historical records/reports** for project history and intent.

Do not convert lower-level evidence into a stronger claim.

Examples:
- Source contains a feature ≠ feature works in production.
- Build PASS ≠ runtime acceptance.
- Unit test PASS ≠ user acceptance.
- Old Handoff claim ≠ current Source truth.

---

## 13. Definition of Done

Unless the project's explicit governance says otherwise:

**Do not mark an item `done` or `closed` solely because:**
- code was written;
- Source compiles;
- publish succeeds;
- static verifier passes;
- automated tests pass.

When runtime acceptance is required, Done requires:
- runtime evidence; and
- explicit user confirmation.

Until then use a state such as:
`implemented_pending_runtime_verification`.

---

## 14. Data safety

PES-1 uses a conservative default for existing software projects.

Without explicit user approval, do not:
- delete or reset tables;
- clear business data;
- rewrite production rows in bulk;
- introduce destructive migrations;
- add startup cleanup that removes data;
- automatically remove ambiguous/orphan records;
- change source-of-truth semantics destructively.

If a destructive action appears necessary:
1. stop;
2. explain why;
3. describe the affected data;
4. request explicit approval.

---

## 15. Version alignment

For mature PES-1 projects, normal development should keep the new Source release and Project Evolution aligned in the same cycle.

Example:

`Source V20 + PE V20 → develop V21 → return Source V21 + PE V21`

If the version chain cannot be proven, stop and report the mismatch rather than guessing.

Bootstrap is special: a project that never used PE may begin with `Project-Evolution-V1.json` even if its Source already has another historical version.

---

## 16. Maintenance cadence

PES-1 default development-audit cadence:

**10000 versions**

This is intentionally infrequent and prevents rapidly increasing software versions from triggering constant maintenance gates.

Earlier targeted audit is still allowed when:
- the user explicitly requests it; or
- a concrete authorized defect/task justifies it.

Runtime checks are separate from this cadence.

Projects may explicitly override this default in their own governance.

---

## 17. Concurrent Analysis Buffer

When analysis and development happen in parallel:

- Development may be working from PE Vx.
- Analysis may continue receiving new requirements.
- Analysis must **not** fork a stale canonical PE while Development is active.
- New analysis is held as a **Temporary Analysis Buffer**.
- When Development returns the latest PE, that PE becomes the merge base.
- Buffered analysis is conflict-checked and merged on top.
- The latest explicit user decision prevails.
- History and traceability are preserved.

This prevents two competing canonical Project Evolution files.

---

## 18. Handoff retirement

Under PES-1, a separate Handoff is not required for normal continuation.

If a temporary Handoff, audit report or development report is created, any durable information should be merged into Project Evolution, such as:
- current release state;
- architecture changes;
- build results;
- DB impact;
- runtime pending work;
- known warnings;
- changed modules/files;
- next actions.

The temporary report may then remain only as supporting evidence.

---

## 19. Normal development cycle

A recommended PES-1 cycle is:

### Start
1. Receive latest Source.
2. Receive latest Project Evolution.
3. Validate their relationship/version chain.
4. Read current state/governance/execution queue.
5. Inspect relevant Source.

### During development
6. Implement only approved scope.
7. Preserve data-safety and architecture constraints.
8. Capture important new findings/decisions.
9. Build/test/verify.

### End
10. Update Project Evolution with:
   - actual implementation;
   - material modules/files changed;
   - decisions;
   - build/test evidence;
   - DB impact;
   - runtime status;
   - known defects;
   - pending verification;
   - next actions.
11. Return new Source + new Project Evolution.
12. Do not claim Done without the required runtime/user evidence.

---

## 20. What not to put in Project Evolution

Avoid using Project Evolution as:
- a source-code dump;
- a log of every trivial edit;
- a replacement for Git;
- a binary attachment store;
- a copy of every chat message;
- a place for unsupported speculation.

Record durable project knowledge and traceability, not noise.

---

## 21. Minimal instruction to give a fresh Development chat

You can give a new Development chat these three items:

1. the software Source;
2. `Project-Evolution-V1.json`;
3. `Project-Evolution-Help-PES-1.md`;

and instruct it:

> Adopt PES-1 for this project. First inspect the actual Source and initialize Project Evolution V1 from evidence. Do not invent missing history or runtime claims. Populate the project identity, current state, system map, architecture, source structure, module state, risks/unknowns and verification evidence. From then on, maintain Project Evolution in the same development cycle as the Source and return Source + Project Evolution as the normal continuation package.

That is the intended zero-to-PES-1 bootstrap path.
