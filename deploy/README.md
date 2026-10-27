# Publishing Plaat

Both apps publish as self-contained, single-file Linux x64 executables. `appsettings*.json`
and other content (e.g. `wwwroot` for the frontend) are copied next to the executable, not
bundled into it — both apps already resolve config relative to their own directory
(`AppDomain.CurrentDomain.BaseDirectory` in `Plaat.Api/Program.cs`), so this "just works".

## Publish

```bash
dotnet publish Plaat.Api -c Release -p:PublishProfile=linux-x64
dotnet publish Plaat.Frontend -c Release -p:PublishProfile=linux-x64
```

Output:
- `Plaat.Api/bin/Release/net10.0/linux-x64/publish/` → `Plaat.Api` executable + appsettings
- `Plaat.Frontend/bin/Release/net10.0/linux-x64/publish/` → `Plaat.Frontend` executable + appsettings + wwwroot

## Automated deploy — `deploy/deploy.sh`

One command builds both apps and rolls them out over SSH:

```bash
cp deploy/deploy.env.example deploy/deploy.env   # then edit: host, user, paths, ports
deploy/deploy.sh                                  # build + deploy api AND frontend
deploy/deploy.sh api                              # just the API
deploy/deploy.sh rollback                         # revert both to the previous build
deploy/deploy.sh list-backups                     # what's retained on the target
```

`deploy/deploy.env` is gitignored — it holds the target host, SSH user/key, remote
folders, systemd unit names, health-check URLs, how many old builds to keep, and the
appsettings-drift policy. Everything machine-specific lives there, nothing in the script.

### SSH auth / sudo

- **SSH:** password auth works — the script multiplexes a single connection
  (`ControlMaster`) so you're prompted **once** per run, not once per command. Set
  `SSH_KEY` (or `ssh-copy-id` the target) to skip the prompt.
- **sudo:** stopping/starting the system units needs `sudo systemctl` on the target.
  The script tries, in order: `systemctl --user` (if `SYSTEMCTL_USER=1`), passwordless
  `sudo`, then `sudo -S` with a password. Provide the password via `SUDO_PASSWORD` in
  `deploy.env`, or be prompted once per run. Better: grant NOPASSWD for just this, on
  the target:

  ```bash
  # on the target, as root:
  echo "${USER} ALL=(root) NOPASSWD: /usr/bin/systemctl" > /etc/sudoers.d/plaat-deploy
  chmod 440 /etc/sudoers.d/plaat-deploy
  ```

What a deploy does, per app:

1. **Preflight** — checks `dotnet`/`rsync` locally, opens the (multiplexed) SSH
   connection, verifies `rsync`/`curl` on the target, and resolves how to run
   `systemctl` (see above).
2. **Build** — `dotnet publish -p:PublishProfile=linux-x64` (Release, self-contained,
   single file).
3. **appsettings drift check** — for every `appsettings*.json` in the publish output,
   diffs this machine's copy against the one currently on the target. On a difference it
   prints the diff and (with `APPSETTINGS_ON_DIFF=prompt`) asks **keep target / overwrite
   with local / abort**. `keep` / `overwrite` skip the prompt and always do that. With no
   TTY it defaults to **keep**. This is what protects the target's
   `appsettings.Secrets.json` (its DB path, image path, JWT key, `ReplacePaths` are all
   machine-specific) from being clobbered by this dev machine's copy.
4. **Stop services** — `frontend` then `api` (frontend `Requires=` api).
5. **Install** — the build is rsynced to a staging dir on the target, then applied to
   the live app dir:
   - every file in the published build is added/updated;
   - the previous build's app files that this build no longer ships are removed
     (tracked via a `.plaat-manifest` the deploy writes into the app dir);
   - **nothing else is written or deleted** — the SQLite DB, `DataProtectionKeys/`,
     logs and any other runtime state in the app dir are left exactly as they are.
     `PROTECT_PATHS_*` is a second guard for the first-ever deploy / hand-placed files.
   Before applying, the currently-installed app files (only those — not the DB) are
   copied to `<REMOTE_BACKUP_DIR>/<app>/<timestamp>/`.
6. **Start + health check** — `api` then `frontend`; each is polled with `curl` after
   start (any HTTP status = up). If an app never responds, that app is **automatically
   rolled back** to the backup this deploy just took and restarted, and the script
   exits non-zero.
7. **Prune** — old backups beyond `KEEP_BACKUPS` (default 3, floor 1) are deleted.

`rollback` / `list-backups` reuse the same backups; a rollback restores app files only
and likewise leaves the DB and other state untouched.

> The DB is never in scope, but a schema migration applied by a new build still changes
> it in place. If you roll a build back across a migration, restore the DB yourself — the
> pre-deploy state isn't captured unless your DB path happens to sit inside the app dir
> and match a manifest entry (it shouldn't). Keep your own DB backups.

## Run as a service

See `deploy/systemd/*.service`. Install with:

```bash
sudo cp deploy/systemd/plaat-api.service deploy/systemd/plaat-frontend.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now plaat-api.service plaat-frontend.service
```

Logs: `journalctl -u plaat-api -f` / `journalctl -u plaat-frontend -f`.

## Publishing Plaat.Mobile (Android)

`Plaat.Mobile` is a native .NET MAUI app (no WebView/Blazor Hybrid) that talks to `Plaat.Api`
directly, replicating the core listening experience of `Plaat.Frontend` (browsing, playlists,
favorites, playback) while omitting admin/stats/settings. It multi-targets
`net10.0-android;net10.0-ios;net10.0-maccatalyst[;net10.0-windows...]` for genuine cross-platform
architecture, but only the Android target is built/signed/tested today — iOS/Mac Catalyst can only
be restored/built on a Mac with Xcode, and Windows only on Windows.

Before building for a real server, edit `Plaat.Mobile/Constants.cs` (`BackendBaseUri`) — there's no
bundled `appsettings.json`-style config on mobile, so the backend URL is a build-time constant.

### One-time environment setup

Requires the .NET Android workload, a JDK, and the Android SDK (platform-tools + a platform +
build-tools). If you don't already have these (e.g. via Android Studio):

```bash
dotnet workload install maui-android

# A portable JDK + Android SDK that need no root/system install, e.g.:
mkdir -p ~/.local/android-toolchain && cd ~/.local/android-toolchain
curl -sL -o jdk.tar.gz "https://aka.ms/download-jdk/microsoft-jdk-17-linux-x64.tar.gz"
tar -xzf jdk.tar.gz && rm jdk.tar.gz

mkdir -p android-sdk/cmdline-tools && cd android-sdk/cmdline-tools
curl -sL -o cmdline-tools.zip "https://dl.google.com/android/repository/commandlinetools-linux-13114758_latest.zip"
unzip -q cmdline-tools.zip && rm cmdline-tools.zip && mv cmdline-tools latest

export JAVA_HOME=~/.local/android-toolchain/jdk-17.0.20+8
export ANDROID_SDK_ROOT=~/.local/android-toolchain/android-sdk
yes | $ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager --sdk_root=$ANDROID_SDK_ROOT --licenses
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager --sdk_root=$ANDROID_SDK_ROOT \
  "platform-tools" "platforms;android-36" "build-tools;36.0.0"
```

(If Android Studio is already installed, `ANDROID_SDK_ROOT`/`JAVA_HOME` are usually already set up
correctly and the above can be skipped.)

### Local test run (Debug, to a connected device/emulator)

```bash
dotnet build Plaat.Mobile -t:Run -f net10.0-android \
  -p:AndroidSdkDirectory=$ANDROID_SDK_ROOT -p:JavaSdkDirectory=$JAVA_HOME
```

### Signed release APK

```bash
# one-time: generate a release keystore. Store it OUTSIDE the repo and never commit it -
# losing it means you can never publish an update under the same signing identity again.
keytool -genkeypair -v -keystore plaat-mobile-release.keystore -alias plaat-mobile \
  -keyalg RSA -keysize 2048 -validity 10000

dotnet publish Plaat.Mobile -f net10.0-android -c Release \
  -p:AndroidSdkDirectory=$ANDROID_SDK_ROOT -p:JavaSdkDirectory=$JAVA_HOME \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=/absolute/path/to/plaat-mobile-release.keystore \
  -p:AndroidSigningKeyAlias=plaat-mobile \
  -p:AndroidSigningKeyPass=<password> \
  -p:AndroidSigningStorePass=<password>
```

Output: `Plaat.Mobile/bin/Release/net10.0-android/publish/com.plaat.mobile-Signed.apk`. Signing
credentials are passed as `-p:` flags at publish time rather than hardcoded in the csproj, matching
this repo's convention of keeping machine-specific secrets out of source control. Without those
flags (e.g. during local `dotnet publish` testing), the build falls back to a default debug key —
fine for sideloading to your own device, not for distributing an update to an already-installed
release build signed with your real key.

### Install on a device

```bash
adb install -r Plaat.Mobile/bin/Release/net10.0-android/publish/com.plaat.mobile-Signed.apk
```

There's no CI/Play Store pipeline for this app — it's a manual, sideload-only release process, same
spirit as the manual `rsync`-based deploy above for the other two apps.
