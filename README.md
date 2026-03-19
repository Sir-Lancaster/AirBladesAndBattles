# AirBladesAndBattles
Built using Godot v4.6-stable

## Coding Standards

Consistent coding standards keep the codebase readable and maintainable for everyone on the team. When code follows a predictable structure and style, it's easier to understand someone else's work, catch bugs during review, and avoid writing logic that's harder to follow than it needs to be. In a game project especially, systems tend to overlap — you'll likely need to read and modify each other's code, so writing with your teammates in mind matters.

No strict style guide is enforced here, but use good judgment: clear naming, logical structure, and code that doesn't require a comment to explain what it's doing are all good targets to aim for.

## Branching & Merging Rules

All significant changes (new features, character entities, level scenes, etc.) should be developed on a separate branch and merged into `main` via a **pull request**. Minor fixes like position tweaks or small corrections can be pushed directly to `main`. Each PR requires at least one approval from another team member before merging. All review conversations must be resolved before a PR can be merged.

History on `main` is kept linear, so rebase your branch or use squash merging before merging in. Force pushes are blocked to prevent rewriting shared history.

PRs automatically get a Copilot code review on each push; use it as a first-pass check, but it doesn't replace a real human review.

*P.S. For my own sanity in resolving git issues, please make sure you run a git pull each time you open the project or switch between different branches.*