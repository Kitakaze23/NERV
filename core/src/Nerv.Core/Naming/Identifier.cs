using Nerv.Core.Data;
using System;
using System.Collections.Generic;
using System.Text;
using Nerv.Core.Chemistry;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Naming
{
    /// <summary>
    /// Распознавание: канонический ключ каждой молекулы → запись базы / иона; ионные ассоциации.
    /// Сообщает о новых известных частицах событием <see cref="SimEventType.MoleculeIdentified"/>.
    /// </summary>
    internal sealed class Identifier
    {
        /// <summary>Ионы ближе этого расстояния (по ближайшим атомам) считаются одной ассоциацией, Å.</summary>
        public const double AssociationDistance = 3.6;

        private HashSet<string> _seenMolecules = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _seenGroups = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<IonicGroup> _groups = new List<IonicGroup>();

        public IReadOnlyList<IonicGroup> Groups => _groups;

        public void IdentifyMolecules(Simulation sim)
        {
            World w = sim.World;
            ChemistryData data = sim.Data;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Molecule m in w.Molecules)
            {
                string key = Canonicalizer.KeyFromWorld(w, m.Atoms);
                m.CanonicalKey = key;
                m.Compound = data?.Compounds.Find(key);
                m.Ion = m.Charge != 0 ? data?.Ions.Find(key) : null;
                int fv = 0;
                foreach (AtomId id in m.Atoms) fv += w.GetFreeValence(id);
                m.FreeValence = fv;
                ClassifyStability(sim, m);
                if (m.Compound == null && m.Ion == null)
                {
                    // Одиночный атом без записи в базе — называем по элементу, но не объявляем в журнале.
                    if (m.Atoms.Count == 1 && m.Charge == 0) m.AtomName = w.GetElement(m.Atoms[0]).NameRu + " (атом)";
                    continue;
                }

                string sig = key + "@" + MinAtom(m.Atoms);
                seen.Add(sig);
                if (!_seenMolecules.Contains(sig))
                    sim.Emit(SimEventType.MoleculeIdentified, m.Atoms[0], AtomId.None, 0, m.Name);
            }
            _seenMolecules = seen;
        }

        /// <summary>Порог «слабой» связи, ккал/моль: O–O 35, N–N 38, F–O 45, Cl–N 46, N–O 48, Cl–O 49.</summary>
        public const double WeakBondEnergy = 50.0;
        /// <summary>Условная энергия распада веществ, помеченных в базе как неустойчивые (диазен, HOF).</summary>
        private const double KnownUnstableEnergy = 40.0;

        private static void ClassifyStability(Simulation sim, Molecule m)
        {
            m.Unstable = false;
            m.DecayBondEnergy = 0;
            if (m.Atoms.Count < 2 || m.Charge != 0 || m.Ion != null) return;
            World w = sim.World;
            if (m.Compound != null)
            {
                bool marked = m.Compound.Hazard != null && m.Compound.Hazard.IndexOf("неустойч", StringComparison.Ordinal) >= 0;
                if (marked && !m.IsRadical)
                {
                    m.Unstable = true;
                    m.DecayBondEnergy = KnownUnstableEnergy;
                }
                return;
            }
            double weakest = double.MaxValue;
            var inMol = new HashSet<AtomId>(m.Atoms);
            foreach (Bond b in w.Bonds)
            {
                if (!b.IsFormed || !inMol.Contains(b.A)) continue;
                double e = BondEnergies.PotentialEnergy(w, b);
                if (e < weakest) weakest = e;
            }
            if (weakest < WeakBondEnergy)
            {
                m.Unstable = true;
                m.DecayBondEnergy = weakest;
            }
        }

        public void UpdateGroups(Simulation sim)
        {
            _groups.Clear();
            if (sim.Settings.Medium == Medium.Water)
            {
                _seenGroups.Clear();
                return;
            }

            World w = sim.World;
            IReadOnlyList<Molecule> mols = w.Molecules;
            var ions = new List<Molecule>();
            foreach (Molecule m in mols) if (m.Charge != 0) ions.Add(m);
            if (ions.Count < 2)
            {
                _seenGroups.Clear();
                return;
            }

            var parent = new int[ions.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            double d2 = AssociationDistance * AssociationDistance;
            for (int a = 0; a < ions.Count; a++)
                for (int b = a + 1; b < ions.Count; b++)
                {
                    if (Math.Sign(ions[a].Charge) == Math.Sign(ions[b].Charge)) continue;
                    if (MinDistance2(w, ions[a], ions[b]) < d2)
                    {
                        int ra = Find(a), rb = Find(b);
                        if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }
                }

            var comps = new SortedDictionary<int, List<Molecule>>();
            for (int i = 0; i < ions.Count; i++)
            {
                int r = Find(i);
                if (!comps.TryGetValue(r, out List<Molecule> list)) comps[r] = list = new List<Molecule>();
                list.Add(ions[i]);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (List<Molecule> members in comps.Values)
            {
                if (members.Count < 2) continue;
                IonicGroup g = Build(sim.Data, members);
                _groups.Add(g);
                if (g.Name == null || g.Charge != 0) continue;

                var atoms = new List<AtomId>();
                foreach (Molecule m in members) atoms.AddRange(m.Atoms);
                string sig = g.Key + "@" + MinAtom(atoms);
                seen.Add(sig);
                if (!_seenGroups.Contains(sig))
                    sim.Emit(SimEventType.MoleculeIdentified, members[0].Atoms[0], AtomId.None, 1, g.Name);
            }
            _seenGroups = seen;
        }

        private static IonicGroup Build(ChemistryData data, List<Molecule> members)
        {
            // Виды ионов и их количество.
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var sample = new Dictionary<string, Molecule>(StringComparer.Ordinal);
            int charge = 0;
            foreach (Molecule m in members)
            {
                string k = m.CanonicalKey ?? m.Formula;
                counts.TryGetValue(k, out int c);
                counts[k] = c + 1;
                sample[k] = m;
                charge += m.Charge;
            }

            int gcd = 0;
            foreach (int c in counts.Values) gcd = Gcd(gcd, c);

            var keyParts = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts)
                for (int i = 0; i < kv.Value / gcd; i++) keyParts.Add(kv.Key);
            keyParts.Sort(StringComparer.Ordinal);

            var group = new IonicGroup
            {
                Members = members,
                Key = string.Join(".", keyParts),
                FormulaUnits = gcd,
                Charge = charge
            };

            // Формула: сначала катионы, потом анионы.
            var sb = new StringBuilder();
            IonInfo cation = null, anion = null;
            int cationKinds = 0, anionKinds = 0;
            foreach (bool cations in new[] { true, false })
            {
                foreach (KeyValuePair<string, int> kv in counts)
                {
                    Molecule m = sample[kv.Key];
                    if ((m.Charge > 0) != cations) continue;
                    IonInfo ion = data?.Ions.Find(kv.Key);
                    if (cations) { cationKinds++; cation = ion; } else { anionKinds++; anion = ion; }
                    string part = ion?.Formula ?? m.Formula;
                    int n = kv.Value / gcd;
                    if (n == 1) sb.Append(part);
                    else if (IsCompound(part)) sb.Append('(').Append(part).Append(')').Append(n);
                    else sb.Append(part).Append(n);
                }
            }
            group.Formula = sb.ToString();

            group.Compound = data?.Compounds.Find(group.Key);
            if (group.Compound != null) group.Name = group.Compound.NameRu;
            else if (charge == 0 && cationKinds == 1 && anionKinds == 1 && cation != null && anion != null)
                group.Name = Capitalize(anion.SaltPartRu + " " + cation.SaltPartRu);
            return group;
        }

        private static bool IsCompound(string part)
        {
            int upper = 0;
            foreach (char c in part)
            {
                if (char.IsDigit(c)) return true;
                if (char.IsUpper(c)) upper++;
            }
            return upper > 1;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        private static double MinDistance2(World w, Molecule a, Molecule b)
        {
            double best = double.MaxValue;
            foreach (AtomId ia in a.Atoms)
            {
                Vec3 pa = w.Positions[w.IndexOf(ia)];
                foreach (AtomId ib in b.Atoms)
                {
                    double d = (w.Positions[w.IndexOf(ib)] - pa).LengthSquared;
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static string MinAtom(IEnumerable<AtomId> atoms)
        {
            AtomId best = AtomId.None;
            foreach (AtomId a in atoms)
                if (best.IsNone || a.CompareTo(best) < 0) best = a;
            return best.Slot + "#" + best.Generation;
        }
    }
}
