### 1. Setup & Exploration

* `git status` — Check the current state of your working directory and staging area.
* `git log --oneline --graph --all` — View a gorgeous, compact ASCII graph of your commit history across all branches.
* `git diff` — See uncommitted changes you've made to tracked files.

### 2. Staging & Committing

* `git add -p` — **Patch mode:** Interactively choose chunks of code to stage, allowing you to create clean, atomic commits.
* `git commit -m "commit message"` — Record your staged changes with a descriptive message.
* `git commit --amend -m "new message"` — Modify the most recent commit (great for fixing typos in commit messages or adding a forgotten file).

### 3. Branching & Navigation

* `git checkout -b <branch-name>` (or `git switch -c <branch-name>`) — Create a new branch and switch to it immediately.
* `git branch -d <branch-name>` — Safely delete a local branch that has already been merged.
* `git stash` — Temporarily shelve uncommitted changes so you can switch branches without losing work.
* `git stash pop` — Apply your most recently stashed changes back to your working directory and remove them from the stash.

### 4. Syncing & Collaboration

* `git pull --rebase` — Fetch remote changes and replay your local commits on top of them, preventing messy merge commits.
* `git push origin <branch-name>` — Push your local branch to the remote repository.
* `git fetch --prune` — Clean up local references to remote branches that have been deleted on GitHub/GitLab.

### 5. Undoing Mistakes (Use with Caution!)

* `git reset HEAD~1` — Undo your last commit but keep your changes safely in your working directory.
* `git checkout -- <file>` (or `git restore <file>`) — Discard uncommitted changes in a specific file and revert to the last committed state.
* `git reflog` — **Your ultimate safety net:** Shows a log of every action that updated HEAD, allowing you to recover lost commits or "accidentally" destroyed branches.

> **Pro Tip:** Save your favorite complex commands as **Git Aliases** in your `~/.gitconfig` file. For example, run `git config --global alias.lg "log --oneline --graph --all"` so you can just type `git lg` instead of the full command!
