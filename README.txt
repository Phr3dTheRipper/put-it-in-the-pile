PUT IT IN THE PILE
The world had enough. We made one anyway.

Double-click PutItInThePile.exe. No installer or npm ritual required.
Windows 10/11 with .NET Framework 4.8 is required.

THE BREAKTHROUGH
- Real Codex quota percentages and reset times.
- A floating, draggable window with an always-on-top toggle.
- A compact strip for maximum efficiency at watching a number.
- PUT IT IN THE PILE adds an animated miniature meter to your local pile.
- A 0% originality score, carefully maintained by design.
- A menu that copies an earnest, completely unnecessary launch post.

USING IT
Drag the top bar to move the window. Click the bottom-left label to toggle
always on top. Press Esc to switch between the full widget and compact strip.
Ctrl+R refreshes usage. Ctrl+D switches between live and clearly labeled demo
data. The three-dot menu contains demo mode, launch-post copy, and help.
Close with the X; it exits rather than hiding in a tray.

REAL NUMBERS, FICTIONAL IMPORTANCE
Live mode uses an existing ChatGPT-backed Codex CLI sign-in. It starts a short-
lived local `codex app-server`, initializes the protocol, reads
`account/rateLimits/read`, and closes that process. It refreshes every 60 seconds.
This makes no inference requests, creates no chats, and sends no Reddit posts.
The CLI handles authentication; this app does not read or copy auth tokens.
The underlying Codex service contacts OpenAI to retrieve current account limits.

The app displays the windows returned by your account. If there is only a
weekly quota, it shows only a weekly quota. Missing quota data is unavailable,
not zero. Failed refreshes retain the last value with an explicit STALE label.
Remaining percentage is 100 minus the returned used percentage.

If Codex is not found, install/sign in to Codex CLI, then refresh. The app looks
for codex.exe on PATH and in the Windows Codex app's local bin directory.
Optionally set the environment variable PILE_CODEX_PATH to your codex.exe.
Use the three-dot menu's demo mode to enjoy pointless software without signing in.
Demo usage values are fictional. Pile count is your local count, not a claim
about the subreddit. Originality and problems-solved counters are satire.

SETTINGS
Pile count, pin preference, window position, and compact mode are saved in
pile-state.json next to the executable. Keep it in a writable folder.
No usage history, account details, or credentials are stored by this app.
To start a fresh local pile, close the app and remove pile-state.json.

SOURCE / BUILD
src/App.cs contains the native WPF app, Codex client, and parser checks.
src/Main.xaml contains the interface. Build.ps1 compiles with the Windows
.NET Framework C# compiler. No third-party packages are required.

From PowerShell in this folder:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1

Optional commands:
  .\PutItInThePile.exe --demo
  .\PutItInThePile.exe --self-test parser-results.txt
  .\PutItInThePile.exe --check-data usage-check.json
  .\PutItInThePile.exe --preview preview.png
  .\PutItInThePile.exe --demo --state alternate-pile-state.json

The self-test command writes a report and exits. The check-data command writes
current percentages to the specified file and exits. Preview renders a demo
window with six fictional meters into a PNG and exits without saving settings.

Usage protocol documentation:
https://learn.chatgpt.com/docs/app-server

Unofficial parody. Not affiliated with OpenAI or the Codex subreddit.

Same numbers. New window.
