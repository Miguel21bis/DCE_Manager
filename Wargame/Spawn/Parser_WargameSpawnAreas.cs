using System.Collections.Generic;
using System.IO;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Extrait les rues/routes et les surfaces à éviter depuis
    // Init/wargame/wargame_spawn.miz.
    //
    // La lecture brute du .miz est faite par WargameDrawingReader, partagé avec
    // le parser de zones. Il ne reste ici que l'interprétation propre au spawn :
    //   - toute polyligne ouverte est une voie carrossable, sans tag
    //   - toute surface fermée est un polygone à traiter à part, son tag étant
    //     lu dans son nom (town, forest, mountain...)
    internal class Parser_WargameSpawnAreas
    {
        public WargameSpawnAreas LoadFromMiz(string mizPath)
        {
            var result = new WargameSpawnAreas();

            List<WargameDrawing> drawings = WargameDrawingReader.ReadFromMiz(mizPath);

            foreach (WargameDrawing drawing in drawings)
            {
                if (!drawing.IsSurface)
                {
                    result.Lines.Add(new WargameSpawnLine
                    {
                        Name = drawing.Name,
                        Points = drawing.Points,
                    });

                    continue;
                }

                string tag = WargameSpawnTag.FromObjectName(drawing.Name);

                if (!WargameSpawnTag.HasKnownTag(drawing.Name))
                {
                    FormUtils.LogRegister("Parser_WargameSpawnAreas | '" + drawing.Name
                        + "' sans tag reconnu, traité en '" + WargameSpawnTag.Default + "'");
                }

                var polygon = new WargameSpawnPolygon
                {
                    Name = drawing.Name,
                    Tag = tag,
                    Policy = WargameSpawnTag.GetPolicy(tag),
                    Points = drawing.Points,
                };

                polygon.ComputeBounds();
                result.Polygons.Add(polygon);
            }

            FormUtils.LogRegister("Parser_WargameSpawnAreas | " + result.Lines.Count + " ligne(s) et "
                + result.Polygons.Count + " polygone(s) lus dans " + Path.GetFileName(mizPath));

            return result;
        }
    }
}
