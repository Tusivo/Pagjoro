# Making a release

Every Pagjoro release is signed. The signing key is yours alone: the private half stays on your computer (never on GitHub), and the public half is built into the program. When the update feature is added, it will install a release only if its signature checks out, so a hijacked GitHub account cannot push a bad update.

## One-time setup: make the signing key

```
cd C:\MyApps\Pagjoro
node scripts/release-sign.js keygen
```

It asks you to choose a passphrase (8 or more characters), then:

- saves the **private key**, locked with that passphrase, to `.pagjoro-signing\release-private.pem` in your user folder;
- writes the **public key** to `src\Pagjoro.Core\release-key.txt`, which the build embeds in the program.

**Back up the private key file and remember the passphrase.** Never put the private key in the repository or on GitHub. Commit `release-key.txt`; it is public by design. The key is made once: `keygen` refuses to overwrite an existing key. This is a different key from any other project's.

## Every release

1. Set the version in `Directory.Build.props` (all four version lines) and in the README status line.
2. Test and build: `dotnet test`, then `powershell -ExecutionPolicy Bypass -File scripts\build.ps1`. Close Pagjoro first, since Windows will not replace a running program.
3. Sign the program:
   ```
   node scripts/release-sign.js sign dist\Pagjoro.exe
   ```
   It asks for the passphrase, writes `dist\Pagjoro.exe.sig` and checks the result against the public key before finishing. The version signed is the one in `Directory.Build.props`.
4. On GitHub, **Draft a new release** with a tag such as `v0.1.0-alpha.1`, make sure the tag points at your final commit, and attach **both** `Pagjoro.exe` and `Pagjoro.exe.sig` (and `Pagjoro.exe.sha256` if you like). Tick **Set as a pre-release** for alpha and beta versions.

A signature is tied to one version and one exact file: change the program after signing and it must be signed again.

## Checking a release yourself

```
node scripts/release-sign.js verify dist\Pagjoro.exe --version 0.1.0-alpha.1
```

## Good to know
- A code-signing certificate (Authenticode) is a separate thing: it makes Windows show your name instead of the SmartScreen warning. This release signature does not replace it, and does not depend on it.
- The `.exe`, `.sig` and key files are never committed (see `.gitignore`).
