﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using UnityEngine.Assertions;
using Verse;

namespace CombatAI.Statistics
{
    public class DataWriter_Path
    {
        public struct PathCell
        {
            /// <summary>
            /// Label: preference
            /// </summary>
            public float pref;

            /// <summary>
            /// Sight: enemies relative visibility.
            /// </summary>
            public float enRel;

            /// <summary>
            /// Sight: enemies abs visibility.
            /// </summary>
            public float enAbs;

            /// <summary>
            /// Sight: frindly relative visibility.
            /// </summary>
            public float frRel;

            /// <summary>
            /// Sight: frindly abs visibility.
            /// </summary>
            public float frAbs;

            /// <summary>
            /// Avoidance: danger.
            /// </summary>
            public float dang;

            /// <summary>
            /// Avoidance: proximity.
            /// </summary>
            public float prox;

            /// <summary>
            /// Avoidance: path.
            /// </summary>
            public float path;
        }
        
        private readonly string writeDir;
        private readonly string name;
        private readonly string filePrefix;

        private int opCounter;
        private readonly List<PathCell> entries = new List<PathCell>(4092);

        public int OpCounter
        {
            get
            {
                return opCounter;
            }
        }

        private string NextFilePath
        {
            get
            {
                return Path.Combine(writeDir, $"{filePrefix}_{opCounter}.bin");
            }
        }


        public DataWriter_Path(string name, string filePrefix)
        {
            this.name = name;
            this.filePrefix = filePrefix;
            string dataPath = Path.Combine(GenFilePaths.ConfigFolderPath, $"data");
            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }
            writeDir = Path.Combine(dataPath, name);
            if (!Directory.Exists(writeDir))
            {
                Directory.CreateDirectory(writeDir);
            }
            else
            {
                opCounter = Directory.GetFiles(writeDir)?.Count(s => s.EndsWith(".bin")) ?? 0;
            }            
            while (File.Exists(Path.Combine(writeDir, $"{filePrefix}_{opCounter++}.bin")))
            {                
            }
        }

        public void Push(PathCell pathCell)
        {
            entries.Add(pathCell);
        }

        public void Write()
        {
            using (FileStream file = File.OpenWrite(NextFilePath))
            {
                StreamWriter writer = new StreamWriter(file);
                writer.WriteLine("pref,enRel,enAbs,frRel,frAbs,dang,prox,path");
                for (int i = 0; i < entries.Count; i++)
                {
                    PathCell cell = entries[i];
                    writer.WriteLine($"{cell.pref},{cell.enRel},{cell.enAbs},{cell.frRel},{cell.frAbs},{cell.dang},{cell.prox},{cell.path}");
                }
                writer.Close();
            }
            opCounter++;
            Clear();
        }

        public void Clear()
        {
            entries.Clear();
        }
    }
}

