# System Compass 1.9.0

- Fix the installer result protocol: the embedded PowerShell scope could emit a null reboot flag and replace the real failure with an unhelpful unknown outcome.
- Support exact WinGet MSIX, MSI, WiX, Burn and Inno upgrades. Conventional supported installers receive explicit restart-suppression switches. Package identity, installed version, target version and post-install outcome are checked. Unsupported types produce an explicit result; failures include WinGet's code and output.
- Select updates with checkboxes, including select-all for visible eligible rows. The update action stays above scrolling content.
- Show a separate progress window with the current program, stage and individual results. Other pages remain usable. Unknown outcomes, lock conflicts and required restarts stop pending items. Closing the main app leaves the active worker running but does not start the remaining queue.
- Correlate progress and results by job ID, so an older Ollama result cannot appear as the selected Python result.
- Apply the setup wizard's English/Turkish choice on the next app launch, even if launch-after-setup is unchecked. Existing report and maintenance settings are preserved.

Download **System-Compass-Setup-1.9.0.exe** for the normal Windows installer with its .NET runtime included. Finish active maintenance and close System Compass before upgrading. Setup does not force-close workers or restart Windows.

Validation uses WPF controls and isolated protocol/argument fixtures; it does not perform real application upgrades on the developer PC. Interactive installation and real publisher-specific upgrades still require machine acceptance testing. The installer is unsigned. The compatibility ZIP remains for older updaters.