# Find and Replace Block 0.2.9

Focused silent-rename integration update.

- Find and Replace no longer calls the visible AutoLISP command c:RENAMEBLOCKS.
- It now calls the internal function RenameBlocks-Silent from the same RENAMEBLOCKS LSP/FAS.
- Manual RENAMEBLOCKS remains unchanged and still prints its rename list.
- Plugin-triggered rename runs are silent when the updated LSP/FAS is loaded.
- If RenameBlocks-Silent is unavailable, Find and Replace silently continues as before.
- All v0.2.8 behavior is otherwise unchanged.
