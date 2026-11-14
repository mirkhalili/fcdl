# Stage 29 — graph tools, image modules and SAM projects

A grid layout command places existing nodes in insertion order, with user-provided rows and columns, without changing wires. “Clear all” and “general chain” are confirmed destructive actions; the latter replaces the workspace with the original starter chain. Node cards have rounded corners. The weave designer node continues to open the original themed bank/designer, outputting one weave; the bank node can hold many.

Crop takes an image. Choose the side to remove, then click a boundary pixel: that pixel remains, pixels on the chosen side are discarded. Repeated clicks crop the current result. The draft can be reset or canceled. Applied coordinates and a fingerprint are stored; upstream image changes require reapplying crop. Zoom and scroll allow pixel-level selection. Output is limited to four million pixels.

Repeat produces an exact rectangle of source width × columns and height × rows, no interpolation. Straight tiles the source; half-width offsets alternate rows by floor(width/2); half-height offsets alternate columns by floor(height/2). Pixels crossing the output edge wrap around, so there are no blanks. Output is capped at four million pixels.

One report input accepts up to 16 different module outputs; each creates a PDF/print page with the output preview and a concise type-specific summary. Reports are generated on demand; incomplete upstream modules must be configured first. A report is a summary, not a full reproduction of all detailed legacy reports; palette summary lists up to 20 colors. The output is image-based PDF, not searchable text. It does not imply physical-machine verification.

Graph projects now save as `.sam` using the self-contained graph archive (a ZIP containing `graph.json`); older `.jgw` graph files remain readable and Save prompts for `.sam`. The application distinguishes legacy SAM archives (`manifest.json`) from graph SAM archives when opening. It refuses to overwrite a legacy or unknown file with a graph. Legacy SAM remains in the old workspace and is not automatically converted; older software versions cannot open the new graph SAM format.
