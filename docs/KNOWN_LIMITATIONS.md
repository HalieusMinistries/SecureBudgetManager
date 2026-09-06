# Known limitations

These items cannot be completed in code alone.

- The household must supply any remaining unknown figures (utility due dates, unconfirmed payday rules, discretionary percentage, cash on hand, and similar).
- A code-signing certificate is not present in this environment. The signing command is ready in `packaging\Sign-Release.ps1`.
- A second physical Windows machine was not available. Isolated-profile verification covers install, launch and data-directory isolation on this computer.
- Tax years after 2026 are not tabulated. The programme warns instead of inventing rates.
- Non-Utah state withholding uses only an explicit flat rate when the household records one.
- Windows Hello is not implemented and is not required for this local-only design.
- CSV is incomplete versus the full local document; exclusions are listed in `docs/BACKUP_RESTORE.md`.
- The local SQLite file is readable to anyone with access to the Windows files. That is the chosen design, not an unfinished encryption feature.
