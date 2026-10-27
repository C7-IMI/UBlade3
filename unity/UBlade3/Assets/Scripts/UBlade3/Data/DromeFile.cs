using System;
using System.Collections.Generic;

namespace UBlade3.Data
{
    // A drome environment: every DefineObject block in one .ENV file, in file order.
    //
    // LoadArena             HYPERX 0x00484e80  (gameengine.cpp lines 2924-2981)
    // ParseObjectDefinition HYPERX 0x00482170  (gameengine.cpp lines 1340-1446)
    //
    // There is no header or scalar block in .ENV beyond its Drome DefineObject: the drome's own
    // placement (pos/fric/elas, always zero/nought for the shipped files) is that object's
    // SetMotionParams, and the arena collision ellipsoid is hard-coded in
    // src/engine/arenaellipsoidstatic.cpp (semi-axes 70/125/70 * g_flTrackingEllipsoidScale, friction
    // 0.01, elasticity 0.95), not read from the file.
    public sealed class DromeFile
    {
        public const int MaxObjects = 100; // kArenaObjectMax, gameengine.cpp line 2920

        private DromeFile()
        {
        }

        // The catalogue spelling of the file name, e.g. "chidro.env".
        public string FileName { get; private set; }

        public string SourcePath { get; private set; }

        public List<ObjectDefinition> Objects { get; private set; }

        public List<string> Warnings { get; private set; }

        public ObjectDefinition Drome { get; private set; }

        public ObjectDefinition Rok { get; private set; }

        public ObjectDefinition NorthGoal { get; private set; }

        public ObjectDefinition SouthGoal { get; private set; }

        public static DromeFile Load(GameDataRoot root, string fileName, DataParseOptions options)
        {
            DataTextFile file = root.OpenText(fileName);

            return Parse(file, fileName, options);
        }

        public static DromeFile Parse(DataTextFile file, string fileName, DataParseOptions options)
        {
            DromeFile drome = new DromeFile();

            drome.FileName = fileName;
            drome.SourcePath = file.Path;
            drome.Objects = new List<ObjectDefinition>();
            drome.Warnings = new List<string>();

            ObjectDefinitionReader reader = new ObjectDefinitionReader(file, file.Path, options);
            ObjectDefinition definition;

            while ((definition = reader.ReadNext()) != null)
            {
                if (drome.Objects.Count >= MaxObjects)
                {
                    throw new DataFormatException("Too many objects in .ENV file", file.Path, definition.SourceLine);
                }

                drome.Objects.Add(definition);

                switch (definition.Kind)
                {
                    case ObjectKind.Drome:
                        if (drome.Drome == null)
                        {
                            drome.Drome = definition;
                        }
                        else
                        {
                            drome.Warnings.Add("More than one Drome object; the loader keeps the last at slot SLOT_DROME.");
                            drome.Drome = definition;
                        }

                        break;

                    case ObjectKind.Rok:
                        if (drome.Rok == null)
                        {
                            drome.Rok = definition;
                        }
                        else
                        {
                            drome.Warnings.Add("More than one Rok object; the last arena Rok definition overwrites m_pRokDef.");
                            drome.Rok = definition;
                        }

                        break;

                    case ObjectKind.Goal:
                        if (definition.TypeIndex == ObjectTypes.GobNGoal)
                        {
                            drome.NorthGoal = definition;
                        }
                        else
                        {
                            drome.SouthGoal = definition;
                        }

                        break;
                }
            }

            drome.Validate();

            return drome;
        }

        private void Validate()
        {
            if (Drome == null)
            {
                throw new DataFormatException("Drome definition missing from environment " + FileName + ".", SourcePath, 0);
            }

            if (Rok == null)
            {
                throw new DataFormatException("Rok definition missing from environment " + FileName + ".", SourcePath, 0);
            }

            if (NorthGoal == null)
            {
                throw new DataFormatException("NGoal definition missing from environment " + FileName + ".", SourcePath, 0);
            }

            if (SouthGoal == null)
            {
                throw new DataFormatException("SGoal definition missing from environment " + FileName + ".", SourcePath, 0);
            }
        }

        public List<ObjectDefinition> GetObjectsOfKind(ObjectKind kind)
        {
            List<ObjectDefinition> matches = new List<ObjectDefinition>();

            foreach (ObjectDefinition definition in Objects)
            {
                if (definition.Kind == kind)
                {
                    matches.Add(definition);
                }
            }

            return matches;
        }

        public List<ObjectDefinition> GetObjectsOfType(int typeIndex)
        {
            List<ObjectDefinition> matches = new List<ObjectDefinition>();

            foreach (ObjectDefinition definition in Objects)
            {
                if (definition.TypeIndex == typeIndex)
                {
                    matches.Add(definition);
                }
            }

            return matches;
        }

        public ObjectDefinition FindObject(string name)
        {
            foreach (ObjectDefinition definition in Objects)
            {
                if (string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
        }

        public override string ToString()
        {
            return "DromeFile(" + FileName + ", " + Objects.Count + " objects)";
        }
    }
}
