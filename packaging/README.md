# Optional MSIX build

The normal `Crest.exe` does everything except **notification and call mirroring**. Windows only lets an app read your notifications (`UserNotificationListener`) when the app has package identity, which means an MSIX package.

```powershell
# from the repo root, on Windows with the .NET 8 SDK and the Windows SDK
.\packaging\build-msix.ps1 -Publisher "CN=Your Name" -PfxPath .\me.pfx -PfxPassword (Read-Host -AsSecureString)
```

- `-Publisher` must match the subject of the certificate you sign with.
- To make a test certificate: `New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Your Name" -CertStoreLocation Cert:\CurrentUser\My`, export it to a .pfx, and import the public part into **Local Machine → Trusted People** so Windows will install the package.
- No certificate? Turn on Developer Mode and register the unpacked folder: `Add-AppxPackage -Register .\packaging\out\layout\AppxManifest.xml`.

After installing, open Crest → Settings → Live activities → **Mirror notifications & calls**, then allow access when Windows asks (Settings → Privacy → Notifications).

## What mirroring can and can't do

- Shows new toasts (app, title, first line) as a notch pill. Calls from Phone Link, Teams, WhatsApp etc. get a longer, green pill.
- It is **read-only**. Windows gives apps no way to press another app's notification buttons, so Accept/Decline for calls stays in the original notification.
- Nothing is stored or sent anywhere.
