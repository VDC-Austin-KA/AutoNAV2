// AutoNAV All-in-One Installer (Windows .exe)
//
// Single-file Windows installer for the AutoNAV Navisworks Manage plugin.
// Carries the full AutoNAV.bundle directory tree (PackageContents.xml plus
// Contents/V24..V27/ DLL + .addin pairs) embedded at compile time.  On launch:
//
//  1. Closes Navisworks if it's running so files aren't locked.
//  2. Extracts the embedded bundle to
//     %APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\.
//     (Per-user; no admin elevation required.)
//  3. PackageContents.xml inside the bundle is what tells Navisworks 2024-2027
//     which per-version DLL to load.
//  4. Prints a summary and pauses so the user can read it.
//
// Built with `go build -ldflags "-s -w"` for GOOS=windows GOARCH=amd64.
package main

import (
	"bufio"
	"embed"
	"fmt"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

//go:embed all:payload/AutoNAV.bundle
var bundle embed.FS

const (
	bundleName    = "AutoNAV.bundle"
	embedRoot     = "payload/AutoNAV.bundle"
	version       = "3.2.0"
	supportedYears = "2024 / 2025 / 2026 / 2027"
)

func main() {
	exitCode := run()
	pause()
	os.Exit(exitCode)
}

func run() int {
	banner()
	closeNavisworks()

	dest, err := bundleInstallDir()
	if err != nil {
		fmt.Println("ERROR:", err)
		return 1
	}

	// Detect which Navisworks installs are present (info-only; we always install
	// the full bundle so PackageContents.xml can route per-version at runtime).
	detected := detectNavisworks()
	if len(detected) == 0 {
		fmt.Println("  WARNING: No Navisworks Manage 2024-2027 install detected on this machine.")
		fmt.Println("           The bundle will still be installed, but won't be loaded until")
		fmt.Println("           a supported Navisworks version is installed.")
		fmt.Println()
	} else {
		fmt.Printf("  Detected Navisworks Manage: %s\n", strings.Join(detected, ", "))
		fmt.Println()
	}

	// Back up any existing bundle.
	if _, err := os.Stat(dest); err == nil {
		stamp := time.Now().Format("20060102_150405")
		backup := dest + ".backup_" + stamp
		fmt.Printf("  Existing bundle found -- backing up to:\n    %s\n", backup)
		if err := os.Rename(dest, backup); err != nil {
			fmt.Printf("  WARNING: backup rename failed (%v); will overwrite in place.\n", err)
		}
	}

	if err := os.MkdirAll(dest, 0o755); err != nil {
		fmt.Println("ERROR: create destination:", err)
		return 1
	}

	fmt.Println("  Writing bundle...")
	count, bytesWritten, err := extractBundle(dest)
	if err != nil {
		fmt.Println("ERROR:", err)
		return 1
	}

	fmt.Println()
	fmt.Println("===============================================================================")
	fmt.Printf(" Installation complete!  Wrote %d files (%d KB) to:\n", count, (bytesWritten+1023)/1024)
	fmt.Printf("   %s\n", dest)
	fmt.Println()
	fmt.Println(" PackageContents.xml will route Navisworks 2024/25/26/27 to its matching DLL.")
	fmt.Println()
	fmt.Println(" Next steps:")
	fmt.Println("   1. Launch Navisworks Manage")
	fmt.Println("   2. Open the Add-Ins ribbon tab")
	fmt.Println("   3. Click AutoNAV to begin")
	fmt.Println("===============================================================================")
	return 0
}

func banner() {
	fmt.Println()
	fmt.Println("===============================================================================")
	fmt.Printf("              AutoNAV All-in-One Installer  v%s\n", version)
	fmt.Printf("         Targets: Navisworks Manage %s\n", supportedYears)
	fmt.Println("         Format: %APPDATA%\\Autodesk\\ApplicationPlugins\\AutoNAV.bundle\\")
	fmt.Println("===============================================================================")
	fmt.Println()
}

// bundleInstallDir resolves the per-user ApplicationPlugins folder and returns
// the AutoNAV.bundle path inside it.
func bundleInstallDir() (string, error) {
	appData := os.Getenv("APPDATA")
	if appData == "" {
		return "", fmt.Errorf("%%APPDATA%% environment variable is empty")
	}
	return filepath.Join(appData, "Autodesk", "ApplicationPlugins", bundleName), nil
}

// detectNavisworks lists Navisworks Manage years present under Program Files.
func detectNavisworks() []string {
	var found []string
	for _, year := range []string{"2024", "2025", "2026", "2027"} {
		for _, root := range []string{
			`C:\Program Files\Autodesk\Navisworks Manage `,
			`C:\Program Files (x86)\Autodesk\Navisworks Manage `,
		} {
			if info, err := os.Stat(root + year); err == nil && info.IsDir() {
				found = append(found, year)
				break
			}
		}
	}
	return found
}

// closeNavisworks force-kills Roamer.exe if running so files aren't locked.
func closeNavisworks() {
	out, err := exec.Command("tasklist", "/FI", "IMAGENAME eq Roamer.exe", "/NH").Output()
	if err != nil || !strings.Contains(strings.ToLower(string(out)), "roamer.exe") {
		return
	}
	fmt.Println("  Closing Navisworks...")
	_ = exec.Command("taskkill", "/F", "/IM", "Roamer.exe").Run()
	time.Sleep(2 * time.Second)
}

// extractBundle walks the embedded FS and writes every file to dest, preserving
// the relative directory structure.
func extractBundle(dest string) (filesWritten int, bytesWritten int64, err error) {
	err = fs.WalkDir(bundle, embedRoot, func(p string, d fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		rel, relErr := filepath.Rel(embedRoot, p)
		if relErr != nil {
			return relErr
		}
		if rel == "." {
			return nil
		}
		out := filepath.Join(dest, rel)
		if d.IsDir() {
			return os.MkdirAll(out, 0o755)
		}
		data, readErr := bundle.ReadFile(p)
		if readErr != nil {
			return fmt.Errorf("read embedded %s: %w", p, readErr)
		}
		if writeErr := os.WriteFile(out, data, 0o644); writeErr != nil {
			return fmt.Errorf("write %s: %w", out, writeErr)
		}
		filesWritten++
		bytesWritten += int64(len(data))
		fmt.Printf("    + %s  (%d bytes)\n", rel, len(data))
		return nil
	})
	return
}

func pause() {
	fmt.Println()
	fmt.Print("Press Enter to close...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}
