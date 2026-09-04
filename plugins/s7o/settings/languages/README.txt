s7o plugin-pack language files

REV06 - September 4, 2026
Updates Trash HP Bars title and description in all 13 catalogs for the
simple ON/OFF row in HUD Menu REV06. Removes the unused cutoff label.
All other translations, including Paragon Helper, DHStrafe, Pestilence
and Map Cursor, are preserved from REV05. Keep your current TipsHelper.
Extract these TXT files into plugins\s7o\settings\languages and restart
FreeHUD. Preserve personal *.override.txt files.

This folder contains the editable UTF-8 catalogs used by s7o HUD Menu and by
s7o plugins that draw user-facing overlay text. The selected language is the
same code used by FreeHUD in data\selected_language.txt.

Supported catalogs:
  enUS.txt, deDE.txt, esES.txt, esMX.txt, frFR.txt, itIT.txt, koKR.txt,
  plPL.txt, ptBR.txt, ptPT.txt, ruRU.txt, zhTW.txt, and zhCN.txt

Correcting a translation
------------------------
1. Open the file matching the selected HUD language code.
2. Edit only the value after the first = character.
3. Keep the key before = unchanged.
4. Preserve numbered placeholders such as {0}, {1}, and {2}.
5. Save as UTF-8 text.
6. Restart FreeHUD.

Example:
  overlay.kanai.title=CUBO DE KANAI

Personal overrides that survive updates
---------------------------------------
Do not edit the shipped catalog when you want to preserve personal corrections.
Create a second file in this folder named:

  <language-code>.override.txt

Examples:
  esES.override.txt
  koKR.override.txt
  enUS.override.txt

The override file may contain only the entries you want to replace:

  # Personal Spanish corrections
  overlay.autogem.specific_gem=Gema
  overlay.kanai.speed=VELOCIDAD

The loader reads the shipped catalog first and the matching .override.txt file
last. A value in the override file therefore replaces the shipped value. Keep
the override file when installing a newer release and do not overwrite it.

What is localized
-----------------
- HUD Menu categories, controls, descriptions, expanded options, statuses,
  confirmations, plugin-list labels, and macro descriptions.
- User-facing overlay titles, controls, instructions, statuses, warnings, and
  compact table headings drawn by the revised s7o plugins.
- Item-category fallbacks used by Ancient and Primal drop notifications.
- Native game entities such as items, legendary gems, skills, runes, item
  types, and areas prefer FreeHUD's selected-language SNO names when available.

Terminology guidance
--------------------
- Prefer the terminology used by Diablo III in the selected game language.
- Native game entities use FreeHUD NameLocalized/SNO text when it is available.
- For custom overlay wording, prefer official Blizzard terminology or established
  Diablo community terminology over a literal dictionary synonym.
- Keep proper feature names such as Paragon, AutoCast, and OpenGR unchanged when
  that is how players commonly identify them in the selected language.

Layout guidance
---------------
- Keep button.* values short. Prefer one word or a familiar abbreviation.
- Keep compact headings at or below the width of the shipped value.
- Use a concise native term rather than allowing text to cross a button, cell,
  neighboring control, or panel boundary.
- Keep HUD Menu descriptions concise enough for the existing wrapped rows.
- The plugins retain the original English text as the final fallback.

Safety rules
------------
- Blank lines and lines beginning with # or // are ignored.
- Malformed lines and empty values are ignored safely.
- Duplicate keys use the final valid value in the file.
- Missing selected-language entries fall back to enUS.txt, then to the original
  English fallback embedded in the C# source.
- Translation values are display-only. They cannot change click actions,
  internal commands, plugin class names, settings keys, SNO IDs, saved values,
  hotkeys, or automation behavior.
- Keep placeholders exactly numbered. A malformed translated format falls back
  safely to the original English format.

Future plugin revisions
-----------------------
- Translate fixed labels with s7o_Localization.Get/Format (or the local T/TF
  wrappers) before appending counts, numeric values, hotkeys, or other text.
- Example: T("menu.tab.favorites", "Favorites") + "  " + count.
- Do not build "Favorites  3" first and expect an exact-label lookup to find
  the shorter "Favorites" catalog entry.
- Shared button and text-drawing helpers should call DisplayButton or Display at
  the final display boundary. A new direct DrawText call must localize its text.
- Never localize action strings, persistence values, class names, file paths,
  enum values, SNO IDs, or strings used for behavior comparisons.
