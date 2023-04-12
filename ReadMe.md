**Use at your own risk, I can't guarantee it will work correctly for all scenarios and you won't lose your data 😉**

The primary goal of this project is to optimize the workflow for both artists and programmers.

This tool is useful even if you have a single repository and use another GitUI alongside it. It is designed with performance and extensibility in mind. This plugin does not rely on undocumented features, and efforts have been made to keep the code compact and predictable.

**It requires git to be installed and added to PATH environment varialbe!**

Features:
- Main feature - most of the operations listed below can be performed on multiple repositories simultaneously
- Manage branches, tags, and stash
- View log (work in progress)
- Stage and commit
- Merge (choose theirs/mine)
- View diff (hunk handling in progress)
- Fetch, pull, and push
- Configure remote settings
- View process log, showing the exact git commands executed and their output
- Project Browser extension
    - Display current status of repositories, current branch, and remote status
    - Show file statuses and highlight directories containing modified files

In development:
- Hunk and managment
- Blame
- Linux and Mac support (Currently they can't detect file changes because it relies on FileSystemWatcher to detect changes)

![Screenshot](Docs~/GitLog.png)

![Screenshot](Docs~/Staging.png)
