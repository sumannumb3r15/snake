# Snake — installing on each device

Two games in one: **Snake 2D** (flat board, wrap-around edges) and **Snake 3D**
(endless night sky, free flight). The Windows version is a native app; every
other device runs the web version, which installs to the home screen and works
offline afterwards.

---

## The quick way — any device

Open this link and install it from the browser:

**https://claude.ai/code/artifact/888a7719-311d-4648-a181-66e701923285**

It is private to your Claude account. Per-device steps are below.

---

## iPhone / iPad

1. Open the link in **Safari** (this only works in Safari, not Chrome).
2. Tap **Share** (the square with the arrow).
3. Scroll down, tap **Add to Home Screen**, then **Add**.

You get a Snake icon that opens full screen with no browser bars. Apple does
not allow other browsers to install web apps, so Safari is required.

## Android

1. Open the link in **Chrome**.
2. Tap **⋮** (top right) → **Add to Home screen** / **Install app**.
3. Confirm.

Chrome may also offer an install banner on its own after a few seconds.

## Mac

- **Safari 17+:** File → **Add to Dock**. It becomes a real app in the Dock
  and in Launchpad.
- **Chrome / Edge:** open the ⋮ menu → **Cast, Save and Share** → **Install
  page as app**.

## Windows

Run **`install-windows.ps1`** (right-click → Run with PowerShell). It:

- copies the app to `%LOCALAPPDATA%\Snake` (no admin needed),
- adds Start menu and Desktop shortcuts,
- registers it in **Settings → Apps → Installed apps** with a working
  Uninstall button.

To remove: use Settings → Apps, or run `install-windows.ps1 -Uninstall`.

The Windows build is the native one (WinForms for the flat board, WPF 3D for
the night sky) and runs better than the web version. The web version is also
copied to `%LOCALAPPDATA%\Snake\web` if you want it locally.

---

## Playing from your own machine instead of the link

If you would rather not use the hosted link, serve the `web` folder yourself:

```powershell
cd web
.\serve.ps1                 # this PC only:  http://localhost:8080/
```

Run it from an **Administrator** PowerShell to let your phone and Mac connect
over the same Wi-Fi — it prints the address to use, e.g. `http://192.168.1.20:8080/`.

One caveat: Android and desktop Chrome only offer **Install app** on `https://`
or `localhost`. Over a plain LAN address you can still play, and iPhone's *Add
to Home Screen* still works, but Android's install prompt will not appear. The
hosted link above avoids this entirely.

---

## Controls

|            | Snake 2D                          | Snake 3D                                  |
| ---------- | --------------------------------- | ----------------------------------------- |
| Touch      | arrow buttons, or swipe the board | ◀ ▶ bank, ▲ ▼ climb/dive, BOOST hold      |
| Keyboard   | arrows or WASD                    | ← → bank, ↑ ↓ climb/dive, Shift to boost  |
| Back       | **menu** button, or Esc           | **menu** button, or Esc                   |

Shortcuts: `#2d` and `#3d` on the end of the URL jump straight into a game.

High scores are stored per device — on the web version in the browser, on
Windows in `%APPDATA%\SnakeApp`.
