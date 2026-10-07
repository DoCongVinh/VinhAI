# VinhAI deployment files

## Main application

Run `VinhAI.exe` from the `D:\VinhAI` folder. It is a self-contained Windows x64 executable and does not require installing .NET.

## Optional user-admin chat server

The admin/user chat feature needs the separate `ChatServer\VinhAI.ChatServer.exe`. To run it locally for testing, open PowerShell in `D:\VinhAI\ChatServer` and set an admin key of at least 32 characters. Use the exact same key in VinhAI's **Settings > Chat Admin API key**:

```powershell
$env:Chat__AdminKey = Read-Host "Enter the same random admin key that you will enter in VinhAI settings"
$env:Chat__DataPath = Join-Path $env:LOCALAPPDATA "VinhAI\chat.json"
$env:ASPNETCORE_URLS = "http://localhost:5080"
.\VinhAI.ChatServer.exe
```

In VinhAI settings, set the chat server URL to `http://localhost:5080`. Keep the key private. The server data file is stored under `%LOCALAPPDATA%\VinhAI\chat.json`.

The HTTP URL above is only for testing on the same computer. For other computers or Internet access, put the service behind a properly configured HTTPS endpoint; do not expose the local HTTP endpoint publicly.
