# Jacquard Studio Vazir design assets

The current Windows application uses the visual system in `Jacquard_Studio_Vazir_Complete.zip` as its only workspace design:

- `src/JacquardStudio/Assets/Studio/jacquard-mark.svg` and `.png` are the blue Jacquard Studio mark used by the updated shell and startup surfaces.
- `src/JacquardStudio/Assets/Studio/module-icons.json` and `tools-icons.json` are generated from the supplied Vazir prototype's 33 module and 38 tool SVG glyphs. The application renders these paths as WPF geometry and uses them in the graph, library, inspector, and toolbar.
- `src/JacquardStudio/Assets/app.ico` is the executable icon for the same cobalt identity.

The older gold/blue SAM marks, raster module artwork, and their SVG redraws are not part of the updated application. The original user-supplied prototype archive remains at the repository root as the visual and licensing reference.
