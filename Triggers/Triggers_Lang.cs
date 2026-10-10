using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Le choix de langue FR / EN de la fenêtre Triggers.
    //   - le code reste écrit en anglais : Lang.T("English text") rend le texte français si le français est choisi
    //     (et le texte anglais tel quel s'il n'y a pas de traduction : rien ne casse, ça reste lisible)
    //   - pour ajouter une phrase : une ligne A("English", "Français") plus bas
    //   - le choix est gardé dans %AppData%\DCE_Manager\triggers_language.txt
    public static class Lang
    {
        private static readonly Dictionary<string, string> ToFr = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> ToEn = new Dictionary<string, string>(StringComparer.Ordinal);

        public static bool French;

        static Lang()
        {
            Fill();
            Load();
        }

        private static void A(string en, string fr)
        {
            ToFr[en] = fr;

            if (!en.StartsWith("@"))
                ToEn[fr] = en;
        }

        // Le texte dans la langue choisie.
        public static string T(string en)
        {
            string fr;
            return French && en != null && ToFr.TryGetValue(en, out fr) ? fr : en;
        }

        // Un mot isolé (jour, mois, bleue...) : cherché sous le nom "@mot".
        public static string W(string word)
        {
            string fr;
            return French && word != null && ToFr.TryGetValue("@" + word, out fr) ? fr : word;
        }

        // ----- les remarques du vérificateur -----
        // Elles sont gardées en anglais dans les triggers (le code s'appuie dessus : "is not a text"...) ; on ne traduit qu'à l'affichage.
        // Un modèle = une phrase du vérificateur avec ses trous ; $1 $2 = ce qui a été mis dans les trous.
        private static readonly List<KeyValuePair<Regex, string>> ProblemRules = new List<KeyValuePair<Regex, string>>();
        private static readonly List<KeyValuePair<Regex, string>> GroupRules = new List<KeyValuePair<Regex, string>>();
        private static readonly Regex ArgLabelRx = new Regex(@"^(\S+ argument \d+ \()(.*?)(\))(?= must|:)", RegexOptions.Singleline);

        private static void P(string english, string french)
        {
            ProblemRules.Add(new KeyValuePair<Regex, string>(new Regex("^" + english + "$", RegexOptions.Singleline), french));
        }

        private static void G(string english, string french)
        {
            GroupRules.Add(new KeyValuePair<Regex, string>(new Regex("^" + english + "$", RegexOptions.Singleline), french));
        }

        // "ERROR: ..." / "Warning: ..." / "Note: ..." (texte gardé en anglais) -> la phrase dans la langue choisie.
        public static string Problem(string text)
        {
            if (!French || string.IsNullOrEmpty(text))
                return text;

            string tag = "", body = text;

            if (text.StartsWith("ERROR: ", StringComparison.Ordinal)) { tag = "ERREUR : "; body = text.Substring(7); }
            else if (text.StartsWith("Warning: ", StringComparison.Ordinal)) { tag = "Avertissement : "; body = text.Substring(9); }
            else if (text.StartsWith("Note: ", StringComparison.Ordinal)) { tag = "Remarque : "; body = text.Substring(6); }

            // "Action.AddImage argument 2 (camp)" : le nom du paramètre se traduit comme dans l'éditeur
            body = ArgLabelRx.Replace(body, m => m.Groups[1].Value + T(m.Groups[2].Value) + m.Groups[3].Value);

            foreach (var rule in ProblemRules)
            {
                if (rule.Key.IsMatch(body))
                    return tag + rule.Key.Replace(body, rule.Value);
            }

            return tag + body;
        }

        // Le titre d'un groupe de la liste ("Mission 3 or later", "Date 1972-07-15", "Flag 802"...).
        public static string Group(string label)
        {
            if (!French || string.IsNullOrEmpty(label))
                return label;

            string fr;

            if (ToFr.TryGetValue(label, out fr))
                return fr;

            foreach (var rule in GroupRules)
            {
                if (rule.Key.IsMatch(label))
                    return rule.Key.Replace(label, rule.Value);
            }

            return label;
        }

        // Pour les contrôles déjà construits : passe un texte d'une langue à l'autre (texte inconnu = inchangé).
        public static string Swap(string text)
        {
            string other;

            if (string.IsNullOrEmpty(text))
                return text;

            if (French)
                return ToFr.TryGetValue(text, out other) ? other : text;

            return ToEn.TryGetValue(text, out other) ? other : text;
        }

        private static string FilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DCE_Manager", "triggers_language.txt");
        }

        private static void Load()
        {
            try
            {
                string path = FilePath();
                French = File.Exists(path) && File.ReadAllText(path).Trim() == "fr";
            }
            catch
            {
                French = false;
            }
        }

        public static void Save()
        {
            try
            {
                string path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, French ? "fr" : "en");
            }
            catch
            {
                // pas grave : on retombera sur l'anglais au prochain démarrage
            }
        }

        private static void Fill()
        {
            A("Close", "Fermer");
            A("Scan all campaigns", "Scanner toutes les campagnes");
            A("Test tool: reads the triggers of every campaign and lists the counts and the errors on the right. Nothing is written.", "Outil de test : lit les triggers de toutes les campagnes et affiche à droite les totaux et les erreurs. Rien n'est écrit.");
            A("All problems", "Tous les problèmes");
            A("Lists the errors, warnings and notes found in all the triggers of this campaign.", "Liste les erreurs, avertissements et remarques trouvés dans tous les triggers de cette campagne.");
            A("Lists every flag (number or name) with the triggers that read it and the triggers that set it.", "Liste chaque flag (numéro ou nom) avec les triggers qui le lisent et ceux qui le modifient.");
            A("Save changes", "Enregistrer");
            A("Writes Init\\camp_triggers_init.lua (as a list). Backups: .bak = the very first original, .prev = the version before this save.", "Écrit Init\\camp_triggers_init.lua (sous forme de liste). Sauvegardes : .bak = le tout premier original, .prev = la version avant cet enregistrement.");
            A("Discard changes", "Annuler les modifications");
            A("Only triggers with problems (⚠)", "Seulement les triggers avec problèmes (⚠)");
            A("Group by: main condition (auto)", "Grouper par : condition principale (auto)");
            A("Group by: mission", "Grouper par : mission");
            A("Group by: date", "Grouper par : date");
            A("Group by: flag", "Grouper par : flag");
            A("No groups (file order)", "Sans groupes (ordre du fichier)");
            A("How the list is grouped. Click a group title to fold or unfold it.", "Comment la liste est groupée. Cliquer sur un titre de groupe le replie ou le déplie.");
            A("+ New trigger", "+ Nouveau trigger");
            A("Adds an empty trigger (always true, no action) just after the selected one.\r\nIt is written to the file only when you click Save changes.", "Ajoute un trigger vide (toujours vrai, sans action) juste après celui qui est choisi.\r\nIl n'est écrit dans le fichier que si vous cliquez sur Enregistrer.");
            A("Name", "Nom");
            A("Active", "Actif");
            A("Once (plays one time only)", "Une seule fois (ne joue qu'une fois)");
            A("Delete trigger", "Supprimer le trigger");
            A("Removes this trigger from the list. It leaves the file only when you click Save changes.", "Retire ce trigger de la liste. Il ne quitte le fichier que si vous cliquez sur Enregistrer.");
            A("Find", "Chercher");
            A("Previous", "Précédent");
            A("Next", "Suivant");
            A("Next match (F3)", "Résultat suivant (F3)");
            A("Previous match (Shift+F3)", "Résultat précédent (Maj+F3)");
            A("Close the search (Esc)", "Fermer la recherche (Échap)");
            A("LIST", "LISTE");
            A("GRAPH", "GRAPHE");
            A("The triggers, grouped, with their editor.", "Les triggers, groupés, avec leur éditeur.");
            A("The whole campaign at a glance: conditions, triggers and what they change (read only).", "Toute la campagne d'un coup d'œil : conditions, triggers et ce qu'ils changent (lecture seule).");
            A("The Lua text of the whole file, as it would be written (read only).", "Le texte Lua de tout le fichier, tel qu'il serait écrit (lecture seule).");
            A("Find (Ctrl+F)", "Chercher (Ctrl+F)");
            A("There are changes that are not saved. Close anyway?", "Des modifications ne sont pas enregistrées. Fermer quand même ?");
            A("Active\\camp_triggers.lua exists: it is the working copy of the running campaign. Changes made to the Init file only reach it after a restart (First Mission).", "Active\\camp_triggers.lua existe : c'est la copie de travail de la campagne en cours. Les modifications du fichier Init n'y arrivent qu'après un redémarrage (First Mission).");
            A("Could not read the triggers of '", "Impossible de lire les triggers de '");
            A(" error(s), ", " erreur(s), ");
            A(" warning(s)  |  ", " avertissement(s)  |  ");
            A(" text(s) kept as raw Lua  |  format: ", " texte(s) gardé(s) en Lua brut  |  format : ");
            A("Working...", "Travail en cours...");
            A("None", "Aucun");
            A("Filter (name, condition or action)   ", "Filtre (nom, condition ou action)   ");
            A(" block(s)", " bloc(s)");
            A("Search a trigger by name...", "Chercher un trigger par son nom...");
            A("Search a DCE function (Action.TargetActive...)", "Chercher une fonction DCE (Action.TargetActive...)");
            A("Finds a trigger by its name. Several words: all must be there, in any order. Small typos and accents are forgiven. (Ctrl+F)", "Trouve un trigger par son nom. Plusieurs mots : tous doivent y être, dans n'importe quel ordre. Les petites fautes et les accents sont pardonnés. (Ctrl+F)");
            A("Finds the triggers that use a DCE function in their condition or actions, e.g. Action.TargetActive. Several words: all must be there. (Ctrl+Shift+F)", "Trouve les triggers qui utilisent une fonction DCE dans leur condition ou leurs actions, ex. Action.TargetActive. Plusieurs mots : tous doivent y être. (Ctrl+Maj+F)");
            A("Previous trigger with a problem (Shift+F8)", "Trigger précédent avec un problème (Maj+F8)");
            A("Next trigger with a problem (F8)", "Trigger suivant avec un problème (F8)");
            A(" trigger(s) with a problem in this block:", " trigger(s) avec un problème dans ce bloc :");
            A("No other trigger with a problem", "Aucun autre trigger avec un problème");
            A("Search a DCE function (type or pick in the list)", "Chercher une fonction DCE (taper ou choisir dans la liste)");
            A(" action with a problem  (click or F8)", " action avec un problème  (clic ou F8)");
            A(" actions with a problem  (click or F8)", " actions avec un problème  (clic ou F8)");
            A("Click: go to the next action with a problem (F8)", "Clic : va à l'action suivante avec un problème (F8)");
            // ----- étape 2 : GRAPHE, rapports, titres de groupes, remarques du vérificateur -----
            A("All triggers (file order)", "Tous les triggers (ordre du fichier)");
            A("Always (no condition)", "Toujours (sans condition)");
            A("Raw Lua condition", "Condition en Lua brut");
            A("Alternatives (OR)", "Alternatives (OU)");
            A("Other conditions", "Autres conditions");
            A("Mission (other test)", "Mission (autre test)");
            A("Date (other test)", "Date (autre test)");
            G("Other \\(not (.+)\\)", "Autres (pas $1)");
            G("Mission (.+) or later", "Mission $1 ou après");
            G("After mission (.+)", "Après la mission $1");
            G("Before mission (.+)", "Avant la mission $1");
            G("Up to mission (.+)", "Jusqu'à la mission $1");
            G("Not mission (.+)", "Pas la mission $1");
            G("Date before (.+)", "Avant le $1");

            P("No condition\\. The engine needs one \\(write true for 'always'\\)\\.", "Pas de condition. Le moteur en exige une (écrire true pour « toujours »).");
            P("The condition is not valid Lua: (.*)", "La condition n'est pas du Lua valide : $1");
            P("No action\\. The engine stops when this trigger fires\\.", "Aucune action. Le moteur s'arrête quand ce trigger se déclenche.");
            P("The action list is empty: this trigger does nothing\\.", "La liste d'actions est vide : ce trigger ne fait rien.");
            P("Action (\\d+) is not valid Lua: (.*)", "L'action $1 n'est pas du Lua valide : $2");
            P("Unknown function (.*) \\(not in the catalogue\\): kept as raw Lua\\.", "Fonction inconnue $1 (absente du catalogue) : gardée en Lua brut.");
            P("(\\S+) needs (\\d+) argument\\(s\\), found (\\d+)\\.", "$1 demande $2 argument(s), $3 trouvé(s).");
            P("(\\S+) takes (\\d+) argument\\(s\\), found (\\d+) \\(the extra ones are ignored\\)\\.", "$1 accepte $2 argument(s), $3 trouvé(s) (les arguments en trop sont ignorés).");
            P("(\\S+)\\((.*)\\): the order looks reversed\\. The engine expects \\(year, month, day\\)\\.", "$1($2) : l'ordre semble inversé. Le moteur attend (année, mois, jour).");
            P("(\\S+): month (.*) is not between 1 and 12 \\(the engine expects year, month, day\\)\\.", "$1 : le mois $2 n'est pas entre 1 et 12 (le moteur attend année, mois, jour).");
            P("(\\S+): day (.*) is not between 1 and 31 \\(the engine expects year, month, day\\)\\.", "$1 : le jour $2 n'est pas entre 1 et 31 (le moteur attend année, mois, jour).");
            P("(\\S+): year (.*) looks wrong \\(the engine expects year, month, day\\)\\.", "$1 : l'année $2 semble fausse (le moteur attend année, mois, jour).");
            P("(.+) must be a number or a text\\.", "$1 doit être un nombre ou un texte.");
            P("(.+) must be a number\\.", "$1 doit être un nombre.");
            P("(.+) must be true or false\\.", "$1 doit être true ou false.");
            P("(.+) must be a text, not a list\\.", "$1 doit être un texte, pas une liste.");
            P("(.+) must be a text\\.", "$1 doit être un texte.");
            P("(.+): \"(.*)\" is a file name, not a side\\. The engine does NOT add this picture to the briefing \\(use \"blue\", \"red\" or \"all\"\\)\\.", "$1 : « $2 » est un nom de fichier, pas un camp. Le moteur n'ajoute PAS cette image au briefing (utiliser \"blue\", \"red\" ou \"all\").");
            P("(.+): \"(.*)\" is not one of: (.*)\\.", "$1 : « $2 » ne fait pas partie de : $3.");
            P("(.+): the list must contain texts only\\.", "$1 : la liste ne doit contenir que des textes.");
            P("Target \"(.*)\" not found in targetlist_init\\.lua or Active\\\\targetlist\\.lua, and no template of this trigger creates it\\.", "Cible \"$1\" introuvable dans targetlist_init.lua ni dans Active\\targetlist.lua, et aucun template de ce trigger ne la crée.");
            P("Target \"(.*)\" not found in targetlist_init\\.lua \\(name\\), and no template of this trigger creates it\\.", "Cible \"$1\" introuvable dans targetlist_init.lua (name), et aucun template de ce trigger ne la crée.");
            P("Air unit \"(.*)\" not found in oob_air_init\\.lua\\.", "Unité aérienne \"$1\" introuvable dans oob_air_init.lua.");
            P("Airbase \"(.*)\" not found in db_airbases\\.lua\\.", "Base aérienne \"$1\" introuvable dans db_airbases.lua.");
            P("Flag (.*) is read but no trigger of this file sets it \\(it may be set elsewhere\\)\\.", "Le flag $1 est lu mais aucun trigger de ce fichier ne le pose (il peut l'être ailleurs).");
            P("Flag (.*) is increased but never set with SetCampFlag in this file: the engine fails if the flag is still empty\\.", "Le flag $1 est augmenté mais jamais posé avec SetCampFlag dans ce fichier : le moteur échoue si le flag est encore vide.");
            P("Duplicate name: another trigger is also called '(.*)'\\.", "Nom en double : un autre trigger s'appelle aussi '$1'.");
            P("Entry (.*) is not a table, ignored\\.", "L'entrée $1 n'est pas une table, ignorée.");
            P("'name' is not a text\\.", "'name' n'est pas un texte.");
            P("'active' is not true/false\\.", "'active' n'est ni true ni false.");
            P("'once' is not true/false\\.", "'once' n'est ni true ni false.");
            P("'condition' is not a text \\(shown as Lua\\)\\.", "'condition' n'est pas un texte (affiché en Lua).");
            P("Key '(.*)' differs from the 'name' field '(.*)' \\(the key is kept\\)\\.", "La clé '$1' diffère du champ 'name' '$2' (la clé est gardée).");
            P("No 'name' field\\.", "Pas de champ 'name'.");
            P("'action' is neither a text nor a list\\.", "'action' n'est ni un texte ni une liste.");
            P("'action' contains a non numeric key '(.*)' \\(ignored\\)\\.", "'action' contient une clé non numérique '$1' (ignorée).");
            P("An action is not a text \\(shown as Lua\\)\\.", "Une action n'est pas un texte (affichée en Lua).");
            P("'expires' is written as a text \\(old form\\)\\. The engine converts it\\.", "'expires' est écrit sous forme de texte (ancienne forme). Le moteur le convertit.");
            P("'expires' should be a table \\{ day = \\.\\., month = \\.\\., year = \\.\\. \\}: the engine ignores it\\.", "'expires' devrait être une table { day = .., month = .., year = .. } : le moteur l'ignore.");
            P("'expires' needs day, month and year as numbers: the engine ignores it\\.", "'expires' demande day, month et year en nombres : le moteur l'ignore.");
            P("'expires' is not a real date \\(day (.*), month (.*)\\)\\.", "'expires' n'est pas une vraie date (jour $1, mois $2).");

            A("ERROR = the engine fails or ignores it.  Warning = suspect.  Note = information.", "ERREUR = le moteur échoue ou l'ignore.  Avertissement = suspect.  Remarque = information.");
            A("Problems (", "Problèmes (");
            A("File   : ", "Fichier : ");
            A("Format : ", "Format : ");
            A("Names checked against : ", "Noms vérifiés avec : ");
            A("No problem found in this file.", "Aucun problème trouvé dans ce fichier.");
            A("No problem found in this trigger.", "Aucun problème trouvé dans ce trigger.");
            A("Select a trigger on the left to see it.", "Choisissez un trigger à gauche pour le voir.");
            A(" trigger(s)  |  ", " trigger(s)  |  ");
            A("named table (will become a list when saved)", "table nommée (deviendra une liste à l'enregistrement)");
            A("mixed (list + named entries)", "mixte (liste + entrées nommées)");
            A("@list", "liste");
            A("@empty", "vide");
            A("-- (not saved yet: this is the text that Save changes would write)\r\n", "-- (pas encore enregistré : voici le texte que Enregistrer écrirait)\r\n");
            A("READ ERROR\r\n", "ERREUR DE LECTURE\r\n");
            A("Could not build the Lua text: ", "Impossible de construire le texte Lua : ");
            A("Title of a target (titleName), chosen from the target list of the campaign.", "Titre d'une cible (titleName), choisi dans la liste des cibles de la campagne.");
            A("Name of a target (the key in targetlist_init.lua), chosen from the target list.", "Nom d'une cible (la clé dans targetlist_init.lua), choisi dans la liste des cibles.");
            A("Name of an air unit (squadron), chosen from oob_air_init.lua.", "Nom d'une unité aérienne (escadrille), choisi dans oob_air_init.lua.");
            A("Name of an airbase.", "Nom d'une base aérienne.");
            A("A number.", "Un nombre.");
            A("true or false.", "true ou false.");
            A("A campaign flag: a number (802) or a name (zoneA). Pick one already used, or type a new one.", "Un flag de campagne : un nombre (802) ou un nom (zoneA). Choisissez-en un déjà utilisé, ou tapez-en un nouveau.");
            A("One value from the list: ", "Une valeur de la liste : ");
            A("A text.", "Un texte.");
            A("Name   : ", "Nom    : ");
            A("Active : ", "Actif  : ");
            A("Once   : ", "Une fois : ");
            A("no (field missing)", "non (champ absent)");
            A("yes", "oui");
            A("no", "non");
            A("not set (plays again at every mission)", "non défini (rejoue à chaque mission)");
            A("yes (plays one time only)", "oui (ne joue qu'une fois)");
            A("no (plays again at every mission)", "non (rejoue à chaque mission)");
            A("CONDITIONS", "CONDITIONS");
            A("    (none)", "    (aucune)");
            A("    [raw Lua - not split into fields]", "    [Lua brut - non décomposé en champs]");
            A(". [raw Lua - not split into fields]", ". [Lua brut - non décomposé en champs]");
            A("   [written as a single text, not a list]", "   [écrit comme un seul texte, pas une liste]");
            A("EXPIRES (kept as is)", "EXPIRES (gardé tel quel)");
            A("OTHER KEYS (unknown, kept as is)", "AUTRES CLÉS (inconnues, gardées telles quelles)");
            A("PROBLEMS", "PROBLÈMES");
            A("FLAGS used by the triggers : ", "FLAGS utilisés par les triggers : ");
            A("(read = tested in a condition ; set = changed by an action)", "(lu = testé dans une condition ; posé = changé par une action)");
            A("    read by ", "    lu par ");
            A("  |  set by ", "  |  posé par ");
            A("    read by : ", "    lu par : ");
            A("    set by  : ", "    posé par : ");
            A("    Note: no trigger sets it (the mission or the engine may).", "    Remarque : aucun trigger ne le pose (la mission ou le moteur le peut).");
            A("    Note: no trigger reads it.", "    Remarque : aucun trigger ne le lit.");
            A("File not found: ", "Fichier introuvable : ");
            A("The file does not define a table named 'camp_triggers'.", "Le fichier ne définit pas de table nommée 'camp_triggers'.");
            A("The file could not be read, so it cannot be saved.", "Le fichier n'a pas pu être lu, il ne peut donc pas être enregistré.");
            A("The file is not saved as UTF-8. Saving would damage the accented letters.\r\nConvert it to UTF-8 first (for example with Notepad++).", "Le fichier n'est pas en UTF-8. L'enregistrer abîmerait les lettres accentuées.\r\nConvertissez-le d'abord en UTF-8 (par exemple avec Notepad++).");
            A("Saving would lose data that could not be read:\r\n", "L'enregistrement perdrait des données qui n'ont pas pu être lues :\r\n");
            A("Saving would lose data that could not be read, in '", "L'enregistrement perdrait des données qui n'ont pas pu être lues, dans '");
            A("\r\nFix this trigger in the Lua file first.", "\r\nCorrigez d'abord ce trigger dans le fichier Lua.");
            A("A trigger has an empty name.", "Un trigger a un nom vide.");
            A("Two triggers have the same name: '", "Deux triggers ont le même nom : '");
            A("'. Names must be unique. Rename one of them.", "'. Les noms doivent être uniques. Renommez l'un d'eux.");
            A("the new file cannot be read back: ", "le nouveau fichier ne peut pas être relu : ");
            A("Safety check failed, nothing was changed (", "Contrôle de sécurité échoué, rien n'a été modifié (");
            A("Could not write the file: ", "Impossible d'écrire le fichier : ");
            A("Campaigns folder not found:\r\n", "Dossier des campagnes introuvable :\r\n");
            // GRAPHE
            A("Raw Lua", "Lua brut");
            A("Target ", "Cible ");
            A("Air unit ", "Unité aérienne ");
            A("End of campaign: ", "Fin de campagne : ");
            A("Other", "Autre");
            A(" more)", " de plus)");
            A(" triggers  |  ", " triggers  |  ");
            A(" different conditions  |  ", " conditions différentes  |  ");
            A(" different results", " résultats différents");
            A("Flags read but set by no trigger : ", "Flags lus mais posés par aucun trigger : ");
            A("   |   set but read by no trigger : ", "   |   posés mais lus par aucun trigger : ");
            A("Click an item to follow its links.", "Cliquez sur un élément pour suivre ses liens.");
            A("TRIGGER  ", "TRIGGER  ");
            A("When : ", "Quand : ");
            A("Then : ", "Alors : ");
            A("CONDITION  ", "CONDITION  ");
            A("RESULT  ", "RÉSULTAT  ");
            A(" trigger", " trigger");
            A("Triggers : ", "Triggers : ");
            A("Unlocks (they read a flag set here) : ", "Débloque (ils lisent un flag posé ici) : ");
            A("Needs (they set a flag read here) : ", "Dépend de (ils posent un flag lu ici) : ");
            A("No other trigger is linked to this flag (the mission or the engine may set or read it).", "Aucun autre trigger n'est lié à ce flag (la mission ou le moteur peut le poser ou le lire).");
            A("Double-click to edit it in LIST.", "Double-clic pour le modifier dans LISTE.");
            A("No trigger in this campaign.", "Aucun trigger dans cette campagne.");
            A("WHEN  (", "QUAND  (");
            A("THEN  (", "ALORS  (");
            A("Click an item to follow its links (flags are followed from one trigger to the next). Double-click a trigger to edit it. M mission, D date, F flag, T target, A air unit, B base, E end, ! error.", "Cliquez sur un élément pour suivre ses liens (les flags sont suivis d'un trigger au suivant). Double-clic sur un trigger pour le modifier. M mission, D date, F flag, T cible, A unité aérienne, B base, E fin, ! erreur.");
            A("Could not build the graph: ", "Impossible de construire le graphe : ");
            // phrases des conditions
            A(" is ", " est ");
            A(" is not ", " n'est pas ");
            A(" is less than ", " est inférieur à ");
            A(" is at most ", " est au plus ");
            A(" is more than ", " est supérieur à ");
            A(" is at least ", " est au moins ");
            A("  AND  ", "  ET  ");
            A("  OR  ", "  OU  ");
            A("Always runs (no condition).", "S'exécute toujours (aucune condition).");
            A("Runs when ", "S'exécute quand ");
            A("Briefing", "Briefing");
            A("Flags", "Flags");
            A("Templates", "Templates");
            // logigramme des flags
            A("View: conditions > triggers > results", "Vue : conditions > triggers > résultats");
            A("View: flag flowchart", "Vue : logigramme des flags");
            A("Conditions > triggers > results: what each trigger reads and changes. Flag flowchart: which trigger sets a flag that another one reads, step by step.", "Conditions > triggers > résultats : ce que chaque trigger lit et change. Logigramme des flags : quel trigger pose un flag qu'un autre lit, étape par étape.");
            A("Each box is a trigger. An arrow goes from the trigger that sets a flag to the ones that read it. Click a box to light up its chain. Double-click to edit it.", "Chaque case est un trigger. Une flèche va du trigger qui pose un flag à ceux qui le lisent. Clic sur une case : sa chaîne s'allume. Double-clic : la modifier.");
            A("Flag ", "Flag ");
            A(" (set elsewhere)", " (posé ailleurs)");
            A(" (read by nobody)", " (lu par personne)");
            A("  (+", "  (+");
            A(" triggers linked by flags  |  ", " triggers liés par des flags  |  ");
            A(" links  |  ", " liens  |  ");
            A(" triggers use no flag (not shown)", " triggers sans flag (non affichés)");
            A(" flags read but set by no trigger (grey boxes on the left)  |  ", " flags lus mais posés par aucun trigger (cases grises à gauche)  |  ");
            A(" flags set but read by no trigger (grey boxes on the right)", " flags posés mais lus par aucun trigger (cases grises à droite)");
            A(" loop(s): a chain that comes back on itself (dashed links).", " boucle(s) : une chaîne qui revient sur elle-même (liens en pointillé).");
            A("Click a box to light up its whole chain.", "Cliquez sur une case pour allumer toute sa chaîne.");
            A("Reads flags : ", "Lit les flags : ");
            A("Sets flags : ", "Pose les flags : ");
            A("   |   sets flags : ", "   |   pose les flags : ");
            A("Needs, directly (they set a flag it reads) : ", "A besoin de, directement (ils posent un flag qu'il lit) : ");
            A("Unlocks, directly (they read a flag it sets) : ", "Débloque, directement (ils lisent un flag qu'il pose) : ");
            A("Whole chain : ", "Chaîne entière : ");
            A(" before, ", " avant, ");
            A(" after", " après");
            A("No trigger uses a flag in this campaign.", "Aucun trigger n'utilise de flag dans cette campagne.");
            A("START", "DÉPART");
            A("STEP ", "ÉTAPE ");
            A("No match", "Aucun résultat");
            A(" match(es)", " résultat(s)");
            A(" error(s)", " erreur(s)");
            A(" warning(s)", " avertissement(s)");
            A("✔ Valid", "✔ Valide");
            A(" note(s))", " remarque(s))");
            A("(raw Lua)", "(Lua brut)");
            A("Raw Lua text", "Texte Lua brut");
            A("Raw Lua text (for anything not in the lists)", "Texte Lua brut (pour tout ce qui n'est pas dans les listes)");
            A("Add an action", "Ajouter une action");
            A("Add an action ▾", "Ajouter une action ▾");
            A("(not set)", "(non défini)");
            A("(not set: ", "(non défini : ");
            A("[raw Lua] ", "[Lua brut] ");
            A(" (optional)", " (facultatif)");
            A("Returns: ", "Renvoie : ");
            A("DEPRECATED: ", "OBSOLÈTE : ");
            A("(deprecated) ", "(obsolète) ");
            A("no longer used", "n'est plus utilisée");
            A("You can still use it.", "Vous pouvez encore l'utiliser.");
            A("Example: ", "Exemple : ");
            A("File not found in Restricted_loadouts or Loadouts.", "Fichier introuvable dans Restricted_loadouts ou Loadouts.");
            A("Could not read this file.", "Impossible de lire ce fichier.");
            A("Nothing is forbidden in this file.", "Rien n'est interdit dans ce fichier.");
            A("Forbidden in this file (by aircraft type, then pylon):", "Interdit dans ce fichier (par type d'avion, puis par pylône) :");
            A("pylon ", "pylône ");
            A("\r\nOptional.", "\r\nFacultatif.");
            A(" If empty: ", " Si vide : ");
            A("Title of a target (titleName), chosen from the target list of the campaign.", "Titre d'une cible (titleName), choisi dans la liste des cibles de la campagne.");
            A("Name of a target (the key in targetlist_init.lua), chosen from the target list.", "Nom d'une cible (la clé dans targetlist_init.lua), choisi dans la liste des cibles.");
            A("Name of an air unit (squadron), chosen from oob_air_init.lua.", "Nom d'une unité aérienne (escadrille), choisi dans oob_air_init.lua.");
            A("Name of an airbase.", "Nom d'une base aérienne.");
            A("A number.", "Un nombre.");
            A("true or false.", "vrai (true) ou faux (false).");
            A("A campaign flag: a number (802) or a name (zoneA). Pick one already used, or type a new one.", "Un flag de campagne : un nombre (802) ou un nom (zoneA). Choisir un flag déjà utilisé, ou en taper un nouveau.");
            A("One value from the list: ", "Une valeur de la liste : ");
            A("A text.", "Un texte.");
            A("A Lua value, written as it is in the file.", "Une valeur Lua, écrite comme dans le fichier.");
            A("(empty text)", "(texte vide)");
            A(" (not in the list)", " (pas dans la liste)");
            A("A flag number or name is needed here.", "Un numéro ou un nom de flag est nécessaire ici.");
            A("A number is needed here.", "Un nombre est nécessaire ici.");
            A("This value cannot be empty.", "Cette valeur ne peut pas être vide.");
            A("This is not valid Lua:\r\n", "Ce n'est pas du Lua valide :\r\n");
            A("This is not valid Lua, so it was not accepted:\r\n", "Ce n'est pas du Lua valide, il n'a donc pas été accepté :\r\n");
            A("Raw Lua: this action is not in the lists, so it is kept as text. Choose a function above to replace it.", "Lua brut : cette action n'est pas dans les listes, elle est donc gardée comme texte. Choisir une fonction ci-dessus pour la remplacer.");
            A("This replaces the raw Lua of this action with:\r\n", "Ceci remplace le Lua brut de cette action par :\r\n");
            A("\r\n\r\nThe current text will be lost (Discard changes brings it back). Continue?", "\r\n\r\nLe texte actuel sera perdu (Annuler les modifications le ramène). Continuer ?");
            A("Delete this action?\r\n\r\n", "Supprimer cette action ?\r\n\r\n");
            A("\r\n\r\nIt leaves the file only when you click Save changes.", "\r\n\r\nElle ne quitte le fichier que si vous cliquez sur Enregistrer.");
            A("Clone this trigger (a copy is added just after it, with a new name)", "Cloner ce trigger (une copie est ajoutée juste après, avec un nouveau nom)");
            A("Delete the trigger '", "Supprimer le trigger '");
            A("It leaves the file only when you click Save changes. Discard changes brings it back.", "Il ne quitte le fichier que si vous cliquez sur Enregistrer. Annuler les modifications le ramène.");
            A("Triggers - delete", "Triggers - suppression");
            A("Triggers - clone", "Triggers - clonage");
            A("Triggers - save", "Triggers - enregistrement");
            A("Triggers - cannot save", "Triggers - enregistrement impossible");
            A("Triggers - not saved", "Triggers - non enregistré");
            A("New trigger", "Nouveau trigger");
            A("New trigger ", "Nouveau trigger ");
            A(" - copy", " - copie");
            A("Clone the trigger '", "Cloner le trigger '");
            A("'?\r\n\r\nA copy called '", "' ?\r\n\r\nUne copie nommée '");
            A("' is added just after it (you can rename it right away).\r\nIt is written to the file only when you click Save changes.", "' est ajoutée juste après (vous pouvez la renommer tout de suite).\r\nElle n'est écrite dans le fichier que si vous cliquez sur Enregistrer.");
            A("The name cannot be empty.", "Le nom ne peut pas être vide.");
            A("Another trigger is already called '", "Un autre trigger s'appelle déjà '");
            A("'. Names must be unique.", "'. Les noms doivent être uniques.");
            A("This writes ", "Ceci écrit ");
            A(" again, as a list.\r\n\r\n", " de nouveau, sous forme de liste.\r\n\r\n");
            A("Kept as they are: names, active, once, conditions, actions, expires and unknown keys.\r\n", "Conservés tels quels : noms, actif, une seule fois, conditions, actions, expiration et clés inconnues.\r\n");
            A("Not kept: comments and any code outside the camp_triggers table", "Non conservés : les commentaires et tout code en dehors de la table camp_triggers");
            A(" (about ", " (environ ");
            A(" line(s) with comments here)", " ligne(s) avec des commentaires ici)");
            A("Backups: .bak (the very first original, never overwritten) and .prev (the version before this save).\r\n\r\n", "Sauvegardes : .bak (le tout premier original, jamais écrasé) et .prev (la version avant cet enregistrement).\r\n\r\n");
            A("Tip: test on a clone of the campaign first.\r\n\r\nContinue?", "Conseil : testez d'abord sur un clone de la campagne.\r\n\r\nContinuer ?");
            A("Saved at ", "Enregistré à ");
            A(". Backups: camp_triggers_init.lua.bak (first original) and .prev.", ". Sauvegardes : camp_triggers_init.lua.bak (premier original) et .prev.");
            A(" Active\\camp_triggers.lua is not changed: the new triggers apply after a restart (First Mission).", " Active\\camp_triggers.lua n'est pas modifié : les nouveaux triggers s'appliquent après un redémarrage (First Mission).");
            A("Throw away the changes made since the last save?", "Abandonner les modifications faites depuis le dernier enregistrement ?");
            A("THEN", "ALORS");
            A("Adds an action at the end of the list.", "Ajoute une action à la fin de la liste.");
            A("Duplicate", "Dupliquer");
            A("Copies the chosen action just after it.", "Copie l'action choisie juste après elle.");
            A("Move the chosen action up", "Monter l'action choisie");
            A("Move the chosen action down", "Descendre l'action choisie");
            A("Delete", "Supprimer");
            A("Delete the chosen action", "Supprimer l'action choisie");
            A("extra (ignored by the engine)", "en trop (ignoré par le moteur)");
            A("action:", "action :");
            A("A condition cannot be empty (use true for 'always').", "Une condition ne peut pas être vide (utiliser true pour « toujours »).");
            A("Values", "Valeurs");
            A("Values : ", "Valeurs : ");
            A("Other", "Autre");
            A("Raw Lua", "Lua brut");
            A("IF", "SI");
            A("this trigger runs when:", "ce trigger se déclenche quand :");
            A("In plain words", "En mots simples");
            A("Show the Lua code", "Afficher le code Lua");
            A("Hide the Lua code", "Cacher le code Lua");
            A("The whole condition, in plain words.", "Toute la condition, en mots simples.");
            A("ALL", "TOUTES");
            A("AT LEAST ONE", "AU MOINS UNE");
            A("NONE", "AUCUNE");
            A("ALL: every condition must be true (AND).\r\nAT LEAST ONE: one true condition is enough (OR).\r\nNONE: not a single one of them may be true (NOT).", "TOUTES : chaque condition doit être vraie (ET).\r\nAU MOINS UNE : une condition vraie suffit (OU).\r\nAUCUNE : pas une seule ne doit être vraie (NON).");
            A("of these conditions must be true", "de ces conditions doivent être vraies");
            A("of these conditions can be true", "de ces conditions peuvent être vraies");
            A("Remove this group and what is inside", "Supprimer ce groupe et ce qu'il contient");
            A("Remove this condition", "Supprimer cette condition");
            A("Move up", "Monter");
            A("Move down", "Descendre");
            A("AND", "ET");
            A("OR", "OU");
            A("+ group", "+ groupe");
            A("No condition: this trigger always runs. Add one with \"+ condition\".", "Aucune condition : ce trigger se déclenche toujours. En ajouter une avec « + condition ».");
            A("Empty group: add a first condition with \"+ condition\". An empty group is ignored.", "Groupe vide : ajouter une première condition avec « + condition ». Un groupe vide est ignoré.");
            A("Adds a condition in this group.", "Ajoute une condition dans ce groupe.");
            A("Adds a group (ALL by default). You can change it to AT LEAST ONE or NONE afterwards.", "Ajoute un groupe (TOUTES par défaut). Il peut ensuite passer à AU MOINS UNE ou AUCUNE.");
            A("Lua:", "Lua :");
            A("What this condition tests. Change it here: the card keeps its place.", "Ce que teste cette condition. On peut la changer ici : la carte garde sa place.");
            A("is equal to", "est égal à");
            A("is different from", "est différent de");
            A("is less than", "est inférieur à");
            A("is at most", "est au plus");
            A("is more than", "est supérieur à");
            A("is at least", "est au moins");
            A("is set", "est défini");
            A("is not set", "n'est pas défini");
            A("is true", "est vrai");
            A("How the value is compared.", "Comment la valeur est comparée.");
            A("A number, true / false, or a word or text (written between quotes for you).", "Un nombre, vrai / faux, ou un mot ou un texte (les guillemets sont ajoutés pour vous).");
            A("A number, or any Lua value (for example Return.CampFlag(801) + 5).", "Un nombre, ou n'importe quelle valeur Lua (par exemple Return.CampFlag(801) + 5).");
            A("A value is needed here.", "Une valeur est nécessaire ici.");
            A("This condition is not in the lists, so it is kept as Lua text. As soon as it can be read, it becomes a normal card.", "Cette condition n'est pas dans les listes, elle est donc gardée comme texte Lua. Dès qu'elle peut être lue, elle devient une carte normale.");
            A("This condition is not made of known pieces, so it is kept as Lua text. Correct it here: as soon as it can be read, the cards appear.", "Cette condition n'est pas faite de morceaux connus, elle est donc gardée comme texte Lua. La corriger ici : dès qu'elle peut être lue, les cartes apparaissent.");
            A("always", "toujours");
            A("not (", "non (");
            A("none of: ", "aucune parmi : ");
            A(" and ", " et ");
            A(" or ", " ou ");
            A("The condition is kept as Lua text.", "La condition est gardée comme texte Lua.");
            A("Always runs (no condition).", "Se déclenche toujours (aucune condition).");
            A("Runs when ", "Se déclenche quand ");
            A("this group and everything inside it", "ce groupe et tout ce qu'il contient");
            A("Remove this condition?\r\n\r\n", "Supprimer cette condition ?\r\n\r\n");
            A("Drag to move.\r\nDrop it between two conditions, or into a group.", "Glisser pour déplacer.\r\nLe déposer entre deux conditions, ou dans un groupe.");
            A("Mission number     (from mission 5, before mission 3...)", "Numéro de mission     (à partir de la mission 5, avant la mission 3...)");
            A("Date     (after 12 March 1975...)", "Date     (après le 12 mars 1975...)");
            A("Flag     (a number or a word set by another trigger)", "Flag     (un nombre ou un mot défini par un autre trigger)");
            A("Target     (still alive, destroyed...)", "Cible     (encore en vie, détruite...)");
            A("Air unit     (active, playable...)", "Unité aérienne     (active, jouable...)");
            A("First generation     (only the very first mission)", "Première génération     (seulement la toute première mission)");
            A("Lua code     (write the condition yourself)", "Code Lua     (écrire la condition soi-même)");
            A("Navy", "Marine");
            A("Ships", "Navires");
            A("Air bases", "Bases aériennes");
            A("Border", "Frontière");
            A("New in ScriptsMod", "Nouveau dans ScriptsMod");
            A("Found in DC_CheckTriggers.lua. No description there.", "Trouvée dans DC_CheckTriggers.lua. Aucune description à cet endroit.");
            A("More tests...", "Autres tests...");
            A("(loading)", "(chargement)");
            A("ALL of...     (every condition must be true)", "TOUTES...     (chaque condition doit être vraie)");
            A("AT LEAST ONE of...     (one true condition is enough)", "AU MOINS UNE...     (une condition vraie suffit)");
            A("NOT...     (what is inside must be false)", "AUCUNE...     (rien de ce qui est dedans ne doit être vrai)");
            A("the mission instance (1 = first generation)", "l'instance de la mission (1 = première génération)");
            A("the campaign year", "l'année de la campagne");
            A("the campaign month", "le mois de la campagne");
            A("the campaign day", "le jour de la campagne");
            A("the ground targets percent (blue list)", "le pourcentage des cibles au sol (liste bleue)");
            A("the ground targets percent (red list)", "le pourcentage des cibles au sol (liste rouge)");
            A("the 'repair minimum destroyed' setting", "le réglage « repair minimum destroyed »");
            A("the ground targets percent ({1} list)", "le pourcentage des cibles au sol (liste {1})");
            A("the campaign {1}", "la date de la campagne ({1})");
            A("Part of the campaign date.", "Une partie de la date de la campagne.");
            A("Percentage computed by the engine for the ground targets of a side.", "Pourcentage calculé par le moteur pour les cibles au sol d'un camp.");
            A("Counts the generations of the same mission; 1 for the first one.", "Compte les générations de la même mission ; 1 pour la première.");
            A("Setting from conf_mod.lua, replaced by its value when the condition is read.", "Réglage de conf_mod.lua, remplacé par sa valeur quand la condition est lue.");
            A("@day", "jour");
            A("@month", "mois");
            A("@year", "année");
            A("@blue", "bleue");
            A("@red", "rouge");
            A("(missing)", "(manquant)");
            A("No problem found in this trigger.", "Aucun problème trouvé dans ce trigger.");
            A("Language of this window", "Langue de cette fenêtre");
            A("Campaign", "Campagne");
            A("Date and time", "Date et heure");
            A("Air units", "Unités aériennes");
            A("Targets", "Cibles");
            A("Ground", "Sol");
            A("Logistics", "Logistique");
            A("Loadouts", "Armements");
            A("year", "année");
            A("month", "mois");
            A("day", "jour");
            A("air unit", "unité aérienne");
            A("side", "camp");
            A("target", "cible");
            A("unit", "unité");
            A("group", "groupe");
            A("place", "lieu");
            A("weight", "poids");
            A("result", "résultat");
            A("value", "valeur");
            A("number", "nombre");
            A("text", "texte");
            A("clear", "effacer");
            A("file", "fichier");
            A("weather", "météo");
            A("active", "actif");
            A("priority", "priorité");
            A("target name", "nom de la cible");
            A("live (true) or kill (false)", "vivre (true) ou tuer (false)");
            A("playable", "jouable");
            A("from", "depuis");
            A("to", "vers");
            A("number (old, ignored)", "nombre (ancien, ignoré)");
            A("hidden", "caché");
            A("probability", "probabilité");
            A("master group", "groupe maître");
            A("bearing", "cap");
            A("cruise speed", "vitesse de croisière");
            A("patrol speed", "vitesse de patrouille");
            A("start time", "heure de départ");
            A("extra", "en plus");
            A("name", "nom");
            A("the same unit", "la même unité");
            A("both sides", "les deux camps");
            A("the mission number", "le numéro de la mission");
            A("Number of the mission being generated. The first mission is number 1.", "Numéro de la mission en cours de génération. La première mission porte le numéro 1.");
            A("the time of day (in seconds)", "l'heure du jour (en secondes)");
            A("Time of day in seconds since midnight.", "Heure du jour en secondes depuis minuit.");
            A("the day of the month", "le jour du mois");
            A("Day of the month of the campaign date.", "Jour du mois de la date de la campagne.");
            A("the month number", "le numéro du mois");
            A("Month of the campaign date (1 to 12).", "Mois de la date de la campagne (1 à 12).");
            A("the year", "l'année");
            A("Year of the campaign date.", "Année de la date de la campagne.");
            A("the campaign date is {0}-{1}-{2} or later", "la date de la campagne est le {0}-{1}-{2} ou plus tard");
            A("the campaign date is before {0}-{1}-{2}", "la date de la campagne est avant le {0}-{1}-{2}");
            A("True while the campaign date is earlier than the given date (the opposite of DatePassed, no 'not' needed).", "Vrai tant que la date de la campagne est antérieure à la date donnée (l'inverse de DatePassed, sans 'not').");
            A("Active until", "Actif jusqu'au");
            A("(included) then switched off", "(inclus), puis désactivé");
            A("After this day the engine switches the trigger off (active = false). The trigger still works on this day.", "Après ce jour le moteur désactive le trigger (active = false). Le trigger fonctionne encore ce jour-là.");
            A("kept as it is: ", "conservé tel quel : ");
            A("True when the campaign date is on or after the given date. Safe with time jumps.", "Vrai quand la date de la campagne est égale ou postérieure à la date donnée. Sans risque avec les sauts dans le temps.");
            A("Value of a campaign flag, as it was at the START of the mission generation (changes made by other triggers in the same run are not seen). Empty if never set.", "Valeur d'un flag de campagne, telle qu'elle était au DÉBUT de la génération de la mission (les changements faits par d'autres triggers pendant la même génération ne sont pas vus). Vide si jamais défini.");
            A("air unit {0} is active", "l'unité aérienne {0} est active");
            A("True when the air unit is not deactivated.", "Vrai quand l'unité aérienne n'est pas désactivée.");
            A("the ready aircraft of {0}", "les avions prêts de {0}");
            A("Number of ready aircraft in the air unit.", "Nombre d'avions prêts dans l'unité aérienne.");
            A("the alive aircraft of {0} (ready + damaged + reserve)", "les avions vivants de {0} (prêts + endommagés + réserve)");
            A("Number of ready, damaged and reserve aircraft in the air unit.", "Nombre d'avions prêts, endommagés et en réserve dans l'unité aérienne.");
            A("all the alive aircraft of side {0}", "tous les avions vivants du camp {0}");
            A("Total of ready, damaged and reserve aircraft of the active air units of a side.", "Total des avions prêts, endommagés et en réserve des unités aériennes actives d'un camp.");
            A("the base of air unit {0}", "la base de l'unité aérienne {0}");
            A("Name of the airbase the air unit operates from.", "Nom de la base aérienne d'où opère l'unité aérienne.");
            A("air unit {0} is playable", "l'unité aérienne {0} est jouable");
            A("True when the air unit can be flown by a player.", "Vrai quand l'unité aérienne peut être pilotée par un joueur.");
            A("the alive % of target {0}", "le % en vie de la cible {0}");
            A("Percentage of the target still alive (100 = untouched). The target is found by its titleName.", "Pourcentage de la cible encore en vie (100 = intacte). La cible est trouvée par son titleName.");
            A("target {0} is active", "la cible {0} est active");
            A("True when the target is active. The target is found by its titleName.", "Vrai quand la cible est active. La cible est trouvée par son titleName.");
            A("the alive % of base {0}", "le % en vie de la base {0}");
            A("Percentage of the base targets still alive. Accepts a target titleName or an airbase name.", "Pourcentage des cibles de la base encore en vie. Accepte un titleName de cible ou un nom de base aérienne.");
            A("unit {0} is dead", "l'unité {0} est morte");
            A("True when this vehicle or ship unit is dead.", "Vrai quand cette unité (véhicule ou navire) est morte.");
            A("group {0} is hidden", "le groupe {0} est caché");
            A("Hidden status of a vehicle or ship group.", "État caché d'un groupe de véhicules ou de navires.");
            A("the spawn probability of group {0}", "la probabilité d'apparition du groupe {0}");
            A("Spawn probability of a vehicle or ship group, from 0 to 1.", "Probabilité d'apparition d'un groupe de véhicules ou de navires, de 0 à 1.");
            A("ship group {0} is inside the area {1}", "le groupe de navires {0} est dans la zone {1}");
            A("True when the ship group is inside the polygon made of the given reference points.", "Vrai quand le groupe de navires est dans le polygone formé par les points de référence donnés.");
            A("the logistic weight at {0}", "le poids logistique à {0}");
            A("Weight delivered by airlift to the place.", "Poids livré par pont aérien au lieu.");
            A("the airlift progress at {0} (% of {1} kg)", "l'avancement du pont aérien à {0} (% de {1} kg)");
            A("Percentage of the airlift objective reached at the place.", "Pourcentage de l'objectif du pont aérien atteint au lieu.");
            A("do nothing", "ne rien faire");
            A("Empty action.", "Action vide.");
            A("end the campaign: {0}", "terminer la campagne : {0}");
            A("Ends the campaign with this result.", "Termine la campagne avec ce résultat.");
            A("set flag {0} to {1}", "mettre le flag {0} à {1}");
            A("Gives a value to a campaign flag (number, true/false or text).", "Donne une valeur à un flag de campagne (nombre, vrai/faux ou texte).");
            A("add {1} to flag {0}", "ajouter {1} au flag {0}");
            A("Adds a number to a campaign flag. The flag must already have a value, or the engine stops.", "Ajoute un nombre à un flag de campagne. Le flag doit déjà avoir une valeur, sinon le moteur s'arrête.");
            A("add this text to the briefing: {0}", "ajouter ce texte au briefing : {0}");
            A("Adds a paragraph to the briefing. The same text is never added twice.", "Ajoute un paragraphe au briefing. Le même texte n'est jamais ajouté deux fois.");
            A("add this text to the briefing (only if the mission is playable): {0}", "ajouter ce texte au briefing (seulement si la mission est jouable) : {0}");
            A("Adds a paragraph to the briefing only when the generated mission is playable.", "Ajoute un paragraphe au briefing seulement quand la mission générée est jouable.");
            A("add the briefing picture {0} for {1}", "ajouter l'image de briefing {0} pour {1}");
            A("Adds a picture to the briefing of one side or of both sides.", "Ajoute une image au briefing d'un camp ou des deux camps.");
            A("change the weather: {0}", "changer la météo : {0}");
            A("Changes the settings used to generate the weather of the next missions (written to conf_mod.lua). Only the ticked settings change.", "Change les réglages utilisés pour générer la météo des prochaines missions (écrits dans conf_mod.lua). Seuls les réglages cochés changent.");
            A("Weather", "Météo");
            A("Trend", "Tendance");
            A("Variance", "Variance");
            A("Reference temperature (°C)", "Température de référence (°C)");
            A("Instability (hours)", "Instabilité (heures)");
            A("Wind activity (m/s)", "Activité du vent (m/s)");
            A("Wind direction (°)", "Direction du vent (°)");
            A("0 = strong low pressure (storms, fronts, heavy clouds).\r\n100 = strong high pressure (clear skies, stable weather).", "0 = forte dépression (orages, fronts, gros nuages).\r\n100 = fort anticyclone (ciel dégagé, temps stable).");
            A("How far the weather may deviate from the trend.\r\nLow = stable and predictable. High = wide variations.", "Écart possible du temps par rapport à la tendance.\r\nFaible = stable et prévisible. Élevé = grandes variations.");
            A("Reference daytime temperature, in degrees Celsius.", "Température de référence en journée, en degrés Celsius.");
            A("How fast the weather changes over time, in hours.", "Vitesse à laquelle le temps change, en heures.");
            A("Average wind at ground level, in m/s. Higher = stronger and more turbulent.", "Vent moyen au sol, en m/s. Plus c'est élevé, plus il est fort et turbulent.");
            A("Dominant wind direction, in degrees.", "Direction dominante du vent, en degrés.");
            A("Also in the text (ignored by the engine): ", "Aussi dans le texte (ignoré par le moteur) : ");
            A("no change", "aucun changement");
            A("trend", "tendance");
            A("variance", "variance");
            A("temperature", "température");
            A("instability", "instabilité");
            A("wind", "vent");
            A("wind direction", "direction du vent");
            A("Tick the settings to change. Unticked settings keep their current value.", "Cochez les réglages à changer. Les autres gardent leur valeur actuelle.");
            A("One route per line: the names of the reference points, separated by commas. With several lines, one route is chosen at random at each mission.", "Une route par ligne : les noms des points de référence, séparés par des virgules. Avec plusieurs lignes, une route est tirée au hasard à chaque mission.");
            A("At least one reference point is needed.", "Il faut au moins un point de référence.");
            A("The route of the ship group, as reference point names.", "La route du groupe de navires, en noms de points de référence.");
            A("cruise speed (m/s)", "vitesse de croisière (m/s)");
            A("patrol speed (m/s)", "vitesse de patrouille (m/s)");
            A("start time (s, empty = now)", "heure de départ (s, vide = maintenant)");
            A("set target {0} active to {1}", "mettre la cible {0} active à {1}");
            A("Turns a target on or off. The target is found by its titleName.", "Active ou désactive une cible. La cible est trouvée par son titleName.");
            A("set the priority of target {0} to {1}", "mettre la priorité de la cible {0} à {1}");
            A("Changes the priority of a target. The target is found by its titleName.", "Change la priorité d'une cible. La cible est trouvée par son titleName.");
            A("bring back to life (true) or kill (false) target {0}: {1} (value {2})", "ressusciter (true) ou tuer (false) la cible {0} : {1} (valeur {2})");
            A("Resuscitates or kills the units of a target. The target is found by its name (not its titleName).", "Ressuscite ou tue les unités d'une cible. La cible est trouvée par son nom (pas son titleName).");
            A("add the ground target intel of side {0}", "ajouter le renseignement sur les cibles au sol du camp {0}");
            A("Adds the ground targets of a side to the briefing (first mission generation only).", "Ajoute les cibles au sol d'un camp au briefing (première génération de mission seulement).");
            A("repair the ground units", "réparer les unités au sol");
            A("Repairs ground units.", "Répare les unités au sol.");
            A("set air unit {0} active to {1}", "mettre l'unité aérienne {0} active à {1}");
            A("Turns an air unit on or off.", "Active ou désactive une unité aérienne.");
            A("set air unit {0} playable to {1}", "mettre l'unité aérienne {0} jouable à {1}");
            A("Allows or forbids players in an air unit.", "Autorise ou interdit les joueurs dans une unité aérienne.");
            A("move air unit {0} to base {1}", "déplacer l'unité aérienne {0} vers la base {1}");
            A("Moves an air unit to another base. A list of bases can be given.", "Déplace une unité aérienne vers une autre base. Une liste de bases peut être donnée.");
            A("move air unit {0} to base {1}, or deactivate it", "déplacer l'unité aérienne {0} vers la base {1}, ou la désactiver");
            A("Moves an air unit to another base, or deactivates it when that is not possible.", "Déplace une unité aérienne vers une autre base, ou la désactive si ce n'est pas possible.");
            A("reinforce {1} from {0}", "renforcer {1} depuis {0}");
            A("Sends aircraft from one air unit to another. With one air unit only, it is reinforced from the reserves.", "Envoie des avions d'une unité aérienne à une autre. Avec une seule unité aérienne, elle est renforcée depuis les réserves.");
            A("repair the air units of {0}", "réparer les unités aériennes de {0}");
            A("Repairs the aircraft of one side or of both sides.", "Répare les avions d'un camp ou des deux camps.");
            A("set base {0} and its units active to {1}", "mettre la base {0} et ses unités actives à {1}");
            A("Turns a base, its targets and its air units on or off.", "Active ou désactive une base, ses cibles et ses unités aériennes.");
            A("give base {1} to side {0}", "donner la base {1} au camp {0}");
            A("Changes the side of a base. Move the air units away first.", "Change le camp d'une base. Déplacer d'abord les unités aériennes.");
            A("set group {0} hidden to {1}", "mettre le groupe {0} caché à {1}");
            A("Hides or shows a vehicle or ship group.", "Cache ou montre un groupe de véhicules ou de navires.");
            A("set the spawn probability of group {0} to {1}", "mettre la probabilité d'apparition du groupe {0} à {1}");
            A("Probability from 0 to 1.", "Probabilité de 0 à 1.");
            A("move group {0} to the zone {1}", "déplacer le groupe {0} vers la zone {1}");
            A("Moves a vehicle group to a reference point.", "Déplace un groupe de véhicules vers un point de référence.");
            A("move group {0} next to {1} (bearing {2}, distance {3})", "déplacer le groupe {0} près de {1} (cap {2}, distance {3})");
            A("Moves a vehicle group relative to another group.", "Déplace un groupe de véhicules par rapport à un autre groupe.");
            A("give a sea route to ship group {0}: {1} (speed {2}, patrol speed {3}, start {4})", "donner une route maritime au groupe de navires {0} : {1} (vitesse {2}, vitesse de patrouille {3}, départ {4})");
            A("Gives a movement mission to a ship group. It sails to the points of one alternative (picked at random). Carriers always use their own maximum speed.", "Donne une mission de déplacement à un groupe de navires. Il navigue vers les points d'une variante (tirée au hasard). Les porte-avions utilisent toujours leur vitesse maximale.");
            A("zones", "zones");
            A("default", "par défaut");
            A("no patrol", "pas de patrouille");
            A("now", "maintenant");
            A("start time (ignored: campaign time is used)", "heure de départ (ignorée : le temps de campagne est utilisé)");
            A("+ Add", "+ Ajouter");
            A("Remove", "Retirer");
            A("set", "définie");
            A("Alternative ", "Variante ");
            A("Type one reference point name per line. Empty alternatives are ignored.", "Tapez un nom de point de référence par ligne. Les variantes vides sont ignorées.");
            A("1 point: the group must be exactly on it.", "1 point : le groupe doit être exactement dessus.");
            A("1 point: the group goes to this point.", "1 point : le groupe va à ce point.");
            A("2 points: the group must be on the line between them.", "2 points : le groupe doit être sur la ligne qui les relie.");
            A("2 points: the group sails between the two points.", "2 points : le groupe navigue entre les deux points.");
            A("Several points: they make an area. The group must be inside it.", "Plusieurs points : ils forment une zone. Le groupe doit être dedans.");
            A("Several points: they make an area. The group sails to random spots inside it.", "Plusieurs points : ils forment une zone. Le groupe navigue vers des endroits au hasard dedans.");
            A("One alternative is picked at random at each mission.", "Une variante est tirée au hasard à chaque mission.");
            A("The reference points (Refpoint) of this alternative, one per line.", "Les points de référence (Refpoint) de cette variante, un par ligne.");
            A("Alternatives: one of them is picked at random at each mission.", "Variantes : l'une d'elles est tirée au hasard à chaque mission.");
            A("Speed in metres per second (1 m/s = 1.94 knots).", "Vitesse en mètres par seconde (1 m/s = 1,94 nœuds).");
            A("+ Insert", "+ Insérer");
            A("Not found in base_mission.miz: ", "Introuvable dans base_mission.miz : ");
            A("Name of a ship group, read from base_mission.miz and oob_ground.", "Nom d'un groupe de navires, lu dans base_mission.miz et oob_ground.");
            A("* Written in the text, but not read by the current SetWeather.", "* Écrit dans le texte, mais pas lu par le SetWeather actuel.");
            A("+ Add point", "+ Point");
            A("Alternatives", "Variantes");
            A("Change rate (weatherChangeRate)", "Vitesse de changement (weatherChangeRate)");
            A("Choose or type a reference point, then click \"+ Add point\". You can add as many as you need.", "Choisissez ou tapez un point de référence, puis cliquez sur « + Ajouter ce point ». Vous pouvez en ajouter autant que nécessaire.");
            A("High pressure (pHigh)", "Haute pression (pHigh)");
            A("Low pressure (pLow)", "Basse pression (pLow)");
            A("Hours since the start of the campaign.", "Heures depuis le début de la campagne.");
            A("Minutes (0 to 59).", "Minutes (0 à 59).");
            A("Pick a zone of base_mission.miz or type a name, then press Enter.", "Choisissez une zone de base_mission.miz ou tapez un nom, puis appuyez sur Entrée.");
            A("Points of the area", "Points de la zone");
            A("Points of the selected alternative", "Points de la variante choisie");
            A("Remove point", "Retirer ce point");
            A("Same as pHigh. 0 = strong low pressure, 100 = strong high pressure.\r\nIf both are written, pHigh is read first, then trend replaces it.", "Comme pHigh. 0 = forte dépression, 100 = fort anticyclone.\r\nSi les deux sont écrits, pHigh est lu d'abord, puis trend le remplace.");
            A("The reference points (Refpoint) of this alternative. Select one and press Delete to remove it.", "Les points de référence (Refpoint) de cette variante. Sélectionnez-en un et appuyez sur Suppr pour le retirer.");
            A("Time since the start of the campaign, as hours and minutes. The engine currently uses the current campaign time.", "Temps depuis le début de la campagne, en heures et minutes. Le moteur utilise actuellement le temps de campagne courant.");
            A("Weather trend, 0 to 100.\r\n0 = strong low pressure (storms, fronts, heavy clouds).\r\n100 = strong high pressure (clear skies, stable weather).", "Tendance météo, de 0 à 100.\r\n0 = forte dépression (orages, fronts, gros nuages).\r\n100 = fort anticyclone (ciel dégagé, temps stable).");
            A("Written in the text, but the current SetWeather does not read it.", "Écrit dans le texte, mais le SetWeather actuel ne le lit pas.");
            A("hours : minutes since the start of the campaign", "heures : minutes depuis le début de la campagne");
            A("▸ More settings (trend, variance, wind...)", "▸ Autres réglages (tendance, variance, vent...)");
            A("▾ Hide other settings", "▾ Masquer les autres réglages");
            A("high pressure", "haute pression");
            A("low pressure", "basse pression");
            A("change rate", "vitesse de changement");
            A("this trigger then does:", "puis ce trigger fait :");
            A("Delete this trigger (it leaves the file only when you click Save changes)", "Supprimer ce trigger (il ne quitte le fichier qu'au clic sur Save changes)");
            A("is equal to  (=)", "est égal à  (=)");
            A("is different from  (≠)", "est différent de  (≠)");
            A("is less than  (<)", "est inférieur à  (<)");
            A("is less than or equal to  (≤)", "est inférieur ou égal à  (≤)");
            A("is more than  (>)", "est supérieur à  (>)");
            A("is more than or equal to  (≥)", "est supérieur ou égal à  (≥)");
            A("Show or hide the Lua code of the actions", "Afficher ou masquer le code Lua des actions");
            A("Show or hide the Lua code of the condition", "Afficher ou masquer le code Lua de la condition");
            A("The area, as reference point names.", "La zone, en noms de points de référence.");
            A("kill helicopter unit {0}", "tuer l'unité d'hélicoptère {0}");
            A("Marks a helicopter unit as dead.", "Marque une unité d'hélicoptère comme morte.");
            A("activate the template {0}", "activer le template {0}");
            A("Activates a ground template (moving front). A list of templates activates one at random.", "Active un template au sol (front mobile). Avec une liste de templates, l'un d'eux est activé au hasard.");
            A("deactivate the template {0}", "désactiver le template {0}");
            A("Deactivates a ground template.", "Désactive un template au sol.");
            A("load the border file {0}", "charger le fichier de frontière {0}");
            A("Loads a border file.", "Charge un fichier de frontière.");
            A("restrict the player loadouts with {0}", "restreindre les armements des joueurs avec {0}");
            A("Forbids some items to players, from a restricted loadout file.", "Interdit certains armements aux joueurs, à partir d'un fichier d'armements restreints.");
            A("authorize the loadouts named {0}", "autoriser les armements nommés {0}");
            A("Authorizes some loadouts for the AI.", "Autorise certains armements pour l'IA.");
            A("set the airlift objective at {0} to {1} kg", "mettre l'objectif du pont aérien à {0} à {1} kg");
            A("Sets an airlift objective for a place.", "Définit un objectif de pont aérien pour un lieu.");
        }
    }

    public partial class Triggers_Form
    {
        private ComboBox _comboLang;

        // Le petit sélecteur de langue en haut à droite.
        private void BuildLanguageBox(Panel topPanel)
        {
            _comboLang = new ComboBox { Dock = DockStyle.Right, DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            _comboLang.Items.Add("English");
            _comboLang.Items.Add("Français");
            _comboLang.SelectedIndex = Lang.French ? 1 : 0;
            _comboLang.SelectionChangeCommitted += (s, e) => SwitchLanguage(_comboLang.SelectedIndex == 1);
            _toolTip.SetToolTip(_comboLang, "Language of this window / Langue de cette fenêtre");

            topPanel.Controls.Add(_comboLang);
        }

        private void SwitchLanguage(bool french)
        {
            if (Lang.French == french)
                return;

            Lang.French = french;
            Lang.Save();

            // les listes et menus construits une fois (avec des textes dedans) sont refaits à la demande
            _funcItems = null;
            _condItems = null;
            _menuKinds = null;
            _menuNewGroup = null;
            _menuMore = null;
            _moreFilled = false;
            _menuAdd = BuildAddMenu();

            SuspendLayout();

            try
            {
                TranslateControls(this);

                if (_file != null)
                {
                    UpdateSummary();

                    if (_file.LoadError == null)
                    {
                        _labelNotice.Text = _file.ActiveCopyExists
                            ? Lang.T("Active\\camp_triggers.lua exists: it is the working copy of the running campaign. Changes made to the Init file only reach it after a restart (First Mission).")
                            : "";

                        RefillList(_editing);                       // titres des groupes, filtre, statut
                    }
                }

                if (_editing != null)
                {
                    RefreshThenList(_listThen.SelectedIndex);
                    RefreshIfList(null);
                    ShowDetails();
                    UpdateStatus(_editing);
                }

                _listTriggers.Invalidate();
                ApplyLuaVisibility();
                RefreshPlain();

                if (_file != null && _file.LoadError == null)
                {
                    // les textes construits en code (aperçu, détail, graphe) sont refaits dans la nouvelle langue
                    if (_editing == null)
                        _textDetail.Text = BuildOverview();
                    else
                        _textInfo.Text = BuildDetail(_editing);

                    if (_graphView != null)
                        ShowGraph();
                }

                if (_graphView != null)
                    _graphView.Invalidate();
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        // Passe les textes fixes (boutons, étiquettes, bulles d'aide, listes simples) d'une langue à l'autre.
        // Les cartes (_canvas) sont refaites par RefreshIfList : on ne les touche pas ici.
        private void TranslateControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (ReferenceEquals(c, _canvas) || ReferenceEquals(c, _comboLang))
                    continue;

                if (c is Label || c is ButtonBase)
                    c.Text = Lang.Swap(c.Text);

                string tip = _toolTip.GetToolTip(c);

                if (tip.Length > 0)
                    _toolTip.SetToolTip(c, Lang.Swap(tip));

                ComboBox combo = c as ComboBox;

                if (combo != null && combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.Items.Count > 0 && combo.Items.Cast<object>().All(x => x is string))
                {
                    int selected = combo.SelectedIndex;

                    for (int i = 0; i < combo.Items.Count; i++)
                        combo.Items[i] = Lang.Swap((string)combo.Items[i]);

                    combo.SelectedIndex = selected;
                }

                TranslateControls(c);
            }
        }
    }
}
