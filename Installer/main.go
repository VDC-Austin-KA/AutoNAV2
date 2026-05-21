// AutoNAV All-in-One Installer (Windows .exe)
//
// Single-file Windows installer for the AutoNAV Navisworks Manage plugin.
// Carries AutoNAV.dll and AutoNAV.addin as embedded resources.  On launch:
//
//  1. If not running elevated, self-relaunches via UAC ("Run as administrator"
//     prompt appears), then exits the non-elevated instance.
//  2. Closes Navisworks if it's running so the DLL isn't locked.
//  3. Detects every installed Navisworks Manage 2024-2027.
//  4. For each, writes AutoNAV.dll + AutoNAV.addin into
//     C:\ProgramData\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\,
//     backing up any existing files into Backup_<timestamp>\ first.
//  5. Prints a summary and pauses so the user can read it.
//
// Built with `go build -ldflags "-s -w"` for GOOS=windows GOARCH=amd64.
package main

import (
	"bufio"
	_ "embed"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

//go:embed payload/AutoNAV.dll
var autoNAVDll []byte

//go:embed payload/AutoNAV.addin
var autoNAVAddin []byte

var supportedVersions = []string{"2024", "2025", "2026", "2027"}

const version = "3.0.0"

func main() {
	if !isAdmin() {
		fmt.Println("Requesting administrator privileges...")
		if err := relaunchAsAdmin(); err != nil {
			fmt.Println("Failed to elevate:", err)
			pause()
			os.Exit(1)
		}
		// Original (non-elevated) process exits; the elevated copy takes over.
		return
	}

	exitCode := run()
	pause()
	os.Exit(exitCode)
}

// isAdmin returns true when the current process can open \\.\PHYSICALDRIVE0,
// which on Windows requires administrator privileges.  This is the standard
// cheap admin-check that needs no external dependencies.
func isAdmin() bool {
	f, err := os.Open(`\\.\PHYSICALDRIVE0`)
	if err == nil {
		f.Close()
		return true
	}
	return false
}

// relaunchAsAdmin re-invokes this executable via PowerShell's
// `Start-Process -Verb RunAs`, which triggers the standard UAC consent prompt.
func relaunchAsAdmin() error {
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	// PowerShell's quoting: wrap the exe path in single quotes and double-up
	// any literal single quote in the path.
	psExe := strings.ReplaceAll(exe, "'", "''")
	return exec.Command(
		"powershell.exe", "-NoProfile", "-Command",
		fmt.Sprintf("Start-Process -FilePath '%s' -Verb RunAs", psExe),
	).Run()
}

func run() int {
	banner()
	closeNavisworks()

	var installed []string
	var errored []string

	for _, v := range supportedVersions {
		ok, err := installToVersion(v)
		if err != nil {
			fmt.Printf("  [!] Navisworks %s -- ERROR: %v\n", v, err)
			errored = append(errored, v)
			continue
		}
		if ok {
			installed = append(installed, v)
		}
	}

	fmt.Println()
	fmt.Println("===============================================================================")
	if len(installed) > 0 {
		fmt.Printf(" Installation complete!  Installed to: Navisworks %s\n", strings.Join(installed, ", "))
		fmt.Println()
		fmt.Println(" Next steps:")
		fmt.Println("   1. Launch Navisworks Manage")
		fmt.Println("   2. Open the Add-Ins ribbon tab")
		fmt.Println("   3. Click AutoNAV to begin")
		fmt.Println("===============================================================================")
		return 0
	}
	if len(errored) > 0 {
		fmt.Println(" Installation finished with errors -- see messages above.")
		fmt.Println("===============================================================================")
		return 1
	}
	fmt.Println(" ERROR: No compatible Navisworks installation found.")
	fmt.Println(" Install Navisworks Manage 2024, 2025, 2026, or 2027 and run again.")
	fmt.Println("===============================================================================")
	return 1
}

func banner() {
	fmt.Println()
	fmt.Println("===============================================================================")
	fmt.Printf("              AutoNAV All-in-One Installer  v%s\n", version)
	fmt.Println("         Targets: Navisworks Manage 2024 / 2025 / 2026 / 2027")
	fmt.Println("===============================================================================")
	fmt.Println()
}

// navisworksInstallDir returns the first existing Navisworks Manage install
// directory for the given version, or "" if not installed.
func navisworksInstallDir(version string) string {
	for _, p := range []string{
		`C:\Program Files\Autodesk\Navisworks Manage ` + version,
		`C:\Program Files (x86)\Autodesk\Navisworks Manage ` + version,
	} {
		if info, err := os.Stat(p); err == nil && info.IsDir() {
			return p
		}
	}
	return ""
}

// closeNavisworks force-kills Roamer.exe (the Navisworks process) if it's
// running, so the plugin DLL isn't locked when we copy over it.
func closeNavisworks() {
	out, err := exec.Command("tasklist", "/FI", "IMAGENAME eq Roamer.exe", "/NH").Output()
	if err != nil || !strings.Contains(strings.ToLower(string(out)), "roamer.exe") {
		return
	}
	fmt.Println("Closing Navisworks...")
	_ = exec.Command("taskkill", "/F", "/IM", "Roamer.exe").Run()
	time.Sleep(2 * time.Second)
}

func installToVersion(version string) (bool, error) {
	if navisworksInstallDir(version) == "" {
		fmt.Printf("  [--] Navisworks %s -- not installed, skipped\n", version)
		return false, nil
	}

	dest := fmt.Sprintf(`C:\ProgramData\Autodesk\Navisworks Manage %s\Plugins\AutoNAV`, version)
	if err := os.MkdirAll(dest, 0o755); err != nil {
		return false, fmt.Errorf("create %s: %w", dest, err)
	}

	destDll := filepath.Join(dest, "AutoNAV.dll")
	destAddin := filepath.Join(dest, "AutoNAV.addin")

	// Back up any existing payload before overwriting.
	if fileExists(destDll) || fileExists(destAddin) {
		stamp := time.Now().Format("20060102_150405")
		backup := filepath.Join(dest, "Backup_"+stamp)
		if err := os.MkdirAll(backup, 0o755); err == nil {
			if fileExists(destDll) {
				_ = copyFile(destDll, filepath.Join(backup, "AutoNAV.dll"))
			}
			if fileExists(destAddin) {
				_ = copyFile(destAddin, filepath.Join(backup, "AutoNAV.addin"))
			}
			fmt.Printf("      Backup saved to: %s\n", backup)
		}
	}

	if err := os.WriteFile(destDll, autoNAVDll, 0o644); err != nil {
		return false, fmt.Errorf("write AutoNAV.dll: %w", err)
	}
	if err := os.WriteFile(destAddin, autoNAVAddin, 0o644); err != nil {
		return false, fmt.Errorf("write AutoNAV.addin: %w", err)
	}

	fmt.Printf("  [+] Navisworks %s -- installed (%d KB) -> %s\n",
		version, (len(autoNAVDll)+1023)/1024, dest)
	return true, nil
}

func fileExists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

func copyFile(src, dst string) error {
	data, err := os.ReadFile(src)
	if err != nil {
		return err
	}
	return os.WriteFile(dst, data, 0o644)
}

func pause() {
	fmt.Println()
	fmt.Print("Press Enter to close...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}
