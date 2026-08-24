using System.Drawing;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Documentation du système de wargame, consultable depuis les Forms wargame.
    //
    // ATTENTION : ce texte doit être tenu à jour à CHAQUE changement de modèle
    // (ajout/suppression d'une variable, changement d'une formule). C'est la seule
    // explication dont disposera un campaignMaker qui découvre le système.
    internal class WargameHelp_Form : Form
    {
        public static void ShowHelp(IWin32Window owner)
        {
            using (var form = new WargameHelp_Form())
            {
                form.ShowDialog(owner);
            }
        }

        private const string HelpText =
@"WARGAME - HOW IT WORKS
======================

The wargame manages a dynamic front line BETWEEN missions. Nothing is simulated
in game: DCE_Manager decides who holds what, and how strong each side is.


THE THREE LEVELS
----------------

  Unit       one vehicle (smallest thing in DCS)
  Template   a .stm file, containing several units
  Formation  what you name and track, e.g. 1-22_Inf


THE KEY IDEA: ONE TEMPLATE, MANY REPRESENTED
--------------------------------------------

Only ONE copy of the template is placed in the mission. You cannot put hundreds
of vehicles on the map without killing performance.

So each placed template STANDS FOR several. That is the xN value.

  Example: 1-22_Inf, template ""Cyprus Border Force 1"", xN = 5

  In game        1 template placed  ->  12 vehicles visible
  In the wargame                        it counts as 5 templates


FORCEPOWER
----------

ForcePower is the real strength of a formation.

  ForcePower = xN  x  Power of the template

  Example: xN = 5, template Power = 10  ->  ForcePower = 50

Power comes from the template catalog: it says what ONE copy of that template is
worth. An armor template is worth more than a supply convoy. You set it once,
per template, and never touch it again.

ForcePower is then the LIVING value: it goes down with losses, up with resupply.


LOSSES
------

What is destroyed in game is converted into lost ForcePower.

  Cost of one destroyed vehicle = Power / Units

  Example: Power = 10, Units = 12  ->  each vehicle is worth 0.83

  5 vehicles destroyed  ->  5 x 0.83 = 4.17 lost
  ForcePower: 50 -> 45.8

Destroying a whole template costs exactly its Power. Note that this cost is the
same for a small formation as for a big one: attacking weak formations is
proportionally far more damaging to them.


ZONE VARIABLES
--------------

  Control        who holds the ground: blue side / red side / contested.
                 Displayed names come from camp_init.wargame_config
                 (country_blue / country_red).

  Terrain        plain / hills / mountain / urban / coastal.
                 Multiplies the defensive power of the zone.

  Supply source  0 = ordinary zone. Above 0 = the zone injects supply into the
                 network (port, rear base, friendly border).

  Supply route   none / track / road / highway. How well the zone can be
                 supplied.

  Modifier       local supply adjustment, added on top of the global flow.
                 A zone cut off by the front gets a penalty.

  Irregular      optional asymmetric mode: instead of collapsing, the force
                 switches to insurgency and keeps a residual stock.

  Neighbors      adjacent zones. Used for attack and retreat paths.


TEMPLATE CATALOG VARIABLES
--------------------------

  Power          what ONE copy of this template is worth. Drives ForcePower.
  Attack         offensive coefficient.
  Defense        defensive coefficient.
  Supply cost    how much supply this template consumes.
  Default xN     the xN proposed when you add this template to a zone.
  Units          number of units used for loss calculation. Pre-filled from the
                 .stm file, but EDITABLE: automatic counting includes static
                 objects (buildings, scenery) that may not be real combat units.
  Detected       raw count from the .stm (dynamic / static). Information only.


IDENTITY
--------

  Name           readable, editable (1-22_Inf). This is what DCE keys on.
  FormationId    the real key. Assigned once, never changed, never reused,
                 even after deletion. Not shown, on purpose.

Renaming a formation is safe: the FormationId keeps the link intact.


FILES
-----

  Init/wargame/wargame_zone.miz          zones drawn in the DCS editor
  Init/wargame/wargame_zones_init.lua    starting state + id counter
  Init/wargame/wargame_templates.lua     template catalog
  Init/wargame/wargame_map.jpg           background map image
  Init/wargame/wargame_map_calib.json    DCS <-> pixel calibration
  Active/wargame_zones.lua               current state during the campaign


WHO DOES WHAT
-------------

  DCE_Manager    decides everything: zone control, formation movement,
                 strength, and the exact position of units.
  ScriptsMod     only matches oob_ground with the target list.
";

        public WargameHelp_Form()
        {
            Text = "Wargame - Help";
            Width = 780;
            Height = 700;
            StartPosition = FormStartPosition.CenterParent;

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                // Une TextBox WinForms n'affiche les retours à la ligne qu'en \r\n :
                // sans cette normalisation, tout le texte s'affiche sur une seule ligne.
                Text = HelpText.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                WordWrap = false,
            };

            // Sans ça le texte s'ouvre entièrement sélectionné en bleu
            textBox.GotFocus += (s, e) => textBox.Select(0, 0);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
            };

            var buttonClose = new Button { Text = "Close", Width = 90, Height = 30 };
            buttonClose.Click += (s, e) => Close();
            bottom.Controls.Add(buttonClose);

            Controls.Add(textBox);
            Controls.Add(bottom);
        }
    }
}
