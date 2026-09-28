// DmDelegateBankResolver.cs — a DmBankResolver built from callbacks.
//
// For hosts that already have a virtual filesystem and don't want a subclass:
//
//     DmDelegateBankResolver resolver = new DmDelegateBankResolver(
//         delegate (string reference) {
//             string name = DmBankResolver.ReferenceFileName(reference);
//             return vfs.Exists("music/" + name) ? "music/" + name : null;
//         },
//         delegate (string key) { return vfs.ReadAllBytes(key); });
//
//     player.BankResolver = resolver;
//
// OnResolve may be left null, in which case the reference's bare filename is passed
// straight to OnOpen and returning null from OnOpen means "not found". That is the
// shorter form when your VFS lookup is already one call:
//
//     new DmDelegateBankResolver(delegate (string name) { return vfs.TryRead(name); });
//
// Part of DirectMusicLite.

using System;

namespace DirectMusicLite
{
    /// <summary>Maps an authored DLS reference onto a key for OnOpen, or null if absent.</summary>
    public delegate string DmBankResolveCallback(string reference);

    /// <summary>Reads the bytes behind a key. Return null (or throw) if unavailable.</summary>
    public delegate byte[] DmBankOpenCallback(string resolvedKey);

    public class DmDelegateBankResolver : DmBankResolver
    {
        public DmBankResolveCallback OnResolve;
        public DmBankOpenCallback OnOpen;

        /// <summary>
        /// Called when a band instrument carries no DLS reference and
        /// DefaultCollectionReference didn't resolve. Optional.
        /// </summary>
        public DmBankResolveCallback OnResolveDefaultCollection;

        /// <summary>
        /// Maps a segment's style reference onto a key for OnOpen. Leave null if you load
        /// styles yourself; LoadReferencedStyle then reports that rather than guessing.
        /// </summary>
        public DmBankResolveCallback OnResolveStyle;

        /// <summary>
        /// Whether keys should be compared case-sensitively when skipping banks that are
        /// already stacked. Leave off for path-like keys; turn it on if your VFS treats
        /// "Bass.dls" and "bass.dls" as different entries.
        /// </summary>
        public bool CaseSensitiveKeys;

        public DmDelegateBankResolver() { }

        public DmDelegateBankResolver(DmBankOpenCallback onOpen)
        {
            OnOpen = onOpen;
        }

        public DmDelegateBankResolver(DmBankResolveCallback onResolve, DmBankOpenCallback onOpen)
        {
            OnResolve = onResolve;
            OnOpen = onOpen;
        }

        public override string Resolve(string reference)
        {
            if (OnResolve != null) return OnResolve(reference);

            // No resolve hook: hand the bare filename to OnOpen and let a null return
            // stand for "missing". References are authoring-time paths, so the directory
            // part is nearly always wrong anyway.
            return ReferenceFileName(reference);
        }

        public override byte[] Open(string resolvedKey)
        {
            if (OnOpen == null)
                throw new InvalidOperationException(
                    "DmDelegateBankResolver: OnOpen was not set, so there is nothing to read bytes with.");
            return OnOpen(resolvedKey);
        }

        public override string ResolveStyle(string reference)
        {
            return OnResolveStyle != null ? OnResolveStyle(reference) : null;
        }

        public override bool SuppliesStyles { get { return OnResolveStyle != null; } }

        protected override string ResolveDefaultCollection()
        {
            if (OnResolveDefaultCollection != null) return OnResolveDefaultCollection(null);
            return null;
        }

        protected override bool KeysEqual(string a, string b)
        {
            return CaseSensitiveKeys
                ? string.Equals(a, b, StringComparison.Ordinal)
                : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
