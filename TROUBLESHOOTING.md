# Troubleshooting: Plugin Not Showing in XIVLauncher

## Quick Diagnostic Checklist

Run through these checks in order:

### ✅ Step 1: Verify GitHub Pages URL

Open this URL in your browser:
```
https://andrewpdg.github.io/Supply-Mission-Helper/repo.json
```

**Expected result:** You should see JSON content like this:
```json
[
  {
    "Author": "andrewpdg",
    "Name": "Supply Mission Helper",
    ...
  }
]
```

**If you get 404:**
- GitHub Pages is not enabled or not deployed yet
- File is not in the main branch
- Wait 2-5 minutes and try again

### ✅ Step 2: Verify Release Exists

Open this URL:
```
https://github.com/andrewpdg/Supply-Mission-Helper/releases/latest
```

**Expected result:** You should see a release page

**Then check this URL:**
```
https://github.com/andrewpdg/Supply-Mission-Helper/releases/latest/download/SupplyMissionHelper.zip
```

**Expected result:** Should download a zip file

**If either fails:**
- You haven't created a release yet
- The zip file is named incorrectly
- Follow "Manual Release Creation" below

### ✅ Step 3: Check Dalamud Settings

In-game:
1. Type `/xlsettings`
2. Go to **Experimental** tab
3. Look for your URL in the **Custom Plugin Repositories** list
4. Make sure it says: `https://andrewpdg.github.io/Supply-Mission-Helper/repo.json`
5. Click **Save and Close**

### ✅ Step 4: Refresh Plugin List

1. Type `/xlplugins`
2. Click the **Settings** (gear icon)
3. Find your repository in the list
4. Make sure it's **enabled** (not grayed out)
5. Close and reopen `/xlplugins`

### ✅ Step 5: Check Dalamud Logs

If still not working, check logs:
1. Close the game
2. Open: `%AppData%\XIVLauncher\dalamud.log`
3. Search for errors related to "SupplyMissionHelper" or your repo URL
4. Look for JSON parsing errors

## Common Issues and Solutions

### Issue 1: "repo.json returns 404"

**Solution:**
1. Go to GitHub repo → Settings → Pages
2. Source: `main` branch, `/ (root)` folder
3. Click Save
4. Wait 3-5 minutes
5. Try the URL again

### Issue 2: "No releases found"

**Solution - Create Manual Release:**

1. Build the plugin:
   ```bash
   dotnet build -c Release
   ```

2. Find these files:
   - `bin/Release/net8.0-windows/SupplyMissionHelper.dll`
   - `SupplyMissionHelper.json`

3. Create a zip file named `SupplyMissionHelper.zip` containing both files

4. Go to: https://github.com/andrewpdg/Supply-Mission-Helper/releases/new

5. Fill in:
   - Tag: `v0.0.1`
   - Title: `v0.0.1`
   - Description: `Initial release`

6. Upload the zip file

7. Click **Publish release**

### Issue 3: "Plugin shows but won't install"

**Check repo.json URLs:**
Make sure these exact URLs work:
```
https://github.com/andrewpdg/Supply-Mission-Helper/releases/latest/download/SupplyMissionHelper.zip
```

**Common mistakes:**
- Wrong zip filename (must be exactly `SupplyMissionHelper.zip`)
- URLs in repo.json don't match actual release
- Missing files in the zip

### Issue 4: "Repository shows error in XIVLauncher"

**Check repo.json syntax:**

Your repo.json must be EXACTLY this format (arrays with square brackets):

```json
[
  {
    "Author": "andrewpdg",
    "Name": "Supply Mission Helper",
    "InternalName": "SupplyMissionHelper",
    "AssemblyVersion": "0.0.1.0",
    "Description": "...",
    "ApplicableVersion": "any",
    "RepoUrl": "https://github.com/andrewpdg/Supply-Mission-Helper",
    "DalamudApiLevel": 10,
    "DownloadLinkInstall": "https://github.com/andrewpdg/Supply-Mission-Helper/releases/latest/download/SupplyMissionHelper.zip",
    "DownloadLinkUpdate": "https://github.com/andrewpdg/Supply-Mission-Helper/releases/latest/download/SupplyMissionHelper.zip",
    "DownloadLinkTesting": "https://github.com/andrewpdg/Supply-Mission-Helper/releases/latest/download/SupplyMissionHelper.zip",
    "LoadRequiredState": 0,
    "LoadSync": false,
    "LoadPriority": 0,
    "Punchline": "Calculate materials needed for Grand Company Supply Missions",
    "Tags": ["Grand Company", "Crafting", "Helper", "Supply Missions"],
    "IconUrl": "",
    "DownloadCount": 0,
    "LastUpdated": "0"
  }
]
```

**Note the square brackets `[ ]` at start and end!**

### Issue 5: "Wrong Dalamud API Level"

If Dalamud shows an incompatibility warning:

1. Check current Dalamud API level in `/xldev`
2. Update `DalamudApiLevel` in both:
   - `repo.json`
   - `SupplyMissionHelper.json`
3. Create a new release

Current API level is usually `10`, but check the official Dalamud Discord.

## Testing Locally First

Before publishing, test locally:

1. Build: `dotnet build -c Release`
2. Copy to: `%AppData%\XIVLauncher\devPlugins\SupplyMissionHelper\`
   - `SupplyMissionHelper.dll`
   - `SupplyMissionHelper.json`
3. In game: `/xlplugins` → **Dev Tools** tab
4. Enable "Display loaded plugins"
5. See if your plugin loads

If it doesn't load locally, it won't load from repo either!

## Still Not Working?

### Enable Dalamud Dev Mode:
1. `/xldev` in game
2. Enable verbose logging
3. Check `%AppData%\XIVLauncher\dalamud.log` for errors

### Validate Your Files:

**Check JSON is valid:**
- Copy your repo.json content
- Paste into: https://jsonlint.com/
- Fix any syntax errors

**Check zip contents:**
```
SupplyMissionHelper.zip
├── SupplyMissionHelper.dll  (must exist)
└── SupplyMissionHelper.json (must exist)
```

### Ask for Help:

If still stuck, provide:
1. Your GitHub repo URL
2. The repo.json URL
3. Screenshot of `/xlsettings` → Experimental tab
4. Any error from `dalamud.log`

Post in:
- Dalamud Discord #plugin-dev channel
- GitHub Issues on your repo

## Quick Fix Script

If you're sure everything is set up but it's still not showing, try:

1. Remove the repo URL from `/xlsettings`
2. Close game completely
3. Delete: `%AppData%\XIVLauncher\pluginConfigs\` (backup first!)
4. Restart game
5. Re-add the repo URL
6. Try again

---

## Need to Start Over?

If completely stuck, you can use the official testing plugin repository format:

1. Fork: https://github.com/goatcorp/DalamudPluginsD17
2. Add your plugin to `plugins.json`
3. Follow their submission guidelines

This is more work but guaranteed to work with their infrastructure.
