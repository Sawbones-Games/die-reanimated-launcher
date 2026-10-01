using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DieAuth
{
    /// <summary>
    /// The one entry point the patched client calls.
    ///
    /// The launcher rewrites <c>SteamWrapperClient.get_SteamAuthKeyBuffer</c> — the getter the game's login
    /// requests read their ticket from — so that instead of
    /// returning the game's cached encrypted app ticket it returns
    /// <c>DieAuth.Bootstrap.Ticket(this, cachedBuffer)</c>.
    ///
    /// We mint a Steam SESSION ticket (<c>ISteamUser::GetAuthSessionTicket</c>) through the game's own
    /// Steamworks wrapper — the same call every ordinary Steam game makes — and hand that back. The server
    /// validates it with Valve (<c>BeginAuthSession</c>); nothing here can vouch for anything by itself.
    ///
    /// Every failure path returns the ORIGINAL buffer, so a broken patch degrades to the unpatched login,
    /// never to a crash. All game types are reached by reflection: this assembly references nothing but
    /// mscorlib, and it is found by SHAPE (a field of type <c>ManagedSteam.Steam</c>) rather than by a name.
    /// </summary>
    public static class Bootstrap
    {
        private const int TicketBufferSize = 1024;

        private static readonly object Gate = new object();
        private static bool _failed;
        private static int _mints;

        /// <summary>Called from the patched getter. <paramref name="client"/> is the SteamWrapperClient
        /// instance; <paramref name="original"/> its cached app-ticket buffer.
        ///
        /// A FRESH ticket is minted on every call — never cached. Steam allows exactly one validation per
        /// ticket, ever, so a cached ticket re-presented after the server restarted (or after any other
        /// validator consumed it) would be met with silence and rejected. The game reads this getter once per
        /// login-type request, so "fresh per call" is one ticket per login, which is the intended shape.</summary>
        public static byte[] Ticket(object client, byte[] original)
        {
            lock (Gate)
            {
                if (_failed) return original;
                try
                {
                    byte[] minted = Mint(client);
                    if (minted == null)
                    {
                        _failed = true;
                        Log("mint failed — falling back to the game's own buffer");
                        return original;
                    }
                    _mints++;
                    Log("session ticket #" + _mints + " minted: " + minted.Length + " B, length prefix " + Hex(minted, 4));
                    return minted;
                }
                catch (Exception e)
                {
                    _failed = true;
                    Log("EXCEPTION — falling back to the game's own buffer: " + e);
                    return original;
                }
            }
        }

        // ── the reflection walk ──────────────────────────────────────────────────────────────────────

        private static byte[] Mint(object client)
        {
            if (client == null) { Log("client is null"); return null; }

            // SteamWrapperClient holds the wrapper in a private field of type ManagedSteam.Steam. Its name is
            // not stable between builds; its TYPE is — so find it by type.
            object steam = null;
            foreach (FieldInfo f in client.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (f.FieldType.FullName == "ManagedSteam.Steam") { steam = f.GetValue(client); break; }
            }
            if (steam == null) { Log("no ManagedSteam.Steam field on " + client.GetType().FullName); return null; }

            PropertyInfo userProp = steam.GetType().GetProperty("User", BindingFlags.Instance | BindingFlags.Public);
            object user = userProp == null ? null : userProp.GetValue(steam, null);
            if (user == null) { Log("Steam.User is null (Steam not initialised?)"); return null; }

            // uint GetAuthSessionTicket(IntPtr ticket, int maxTicket, out uint ticketLength)  → handle, 0 = failure
            MethodInfo get = user.GetType().GetMethod("GetAuthSessionTicket",
                BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(IntPtr), typeof(int), typeof(uint).MakeByRefType() }, null);
            if (get == null) { Log("IUser.GetAuthSessionTicket(IntPtr,int,out uint) not found on " + user.GetType().FullName); return null; }

            byte[] buffer = new byte[TicketBufferSize];
            GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                object[] args = { pin.AddrOfPinnedObject(), buffer.Length, 0u };
                uint handle = (uint)get.Invoke(user, args);
                uint length = (uint)args[2];
                Log("GetAuthSessionTicket → handle " + handle + ", " + length + " B");
                if (handle == 0 || length == 0 || length > buffer.Length) return null;
                byte[] ticket = new byte[length];
                Array.Copy(buffer, ticket, (int)length);
                return ticket;
            }
            finally { pin.Free(); }
        }

        // ── diagnostics ──────────────────────────────────────────────────────────────────────────────

        private static string BaseDir()
        {
            try { return Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location); }
            catch { return Path.GetTempPath(); }
        }

        private static void Log(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(BaseDir(), "DieAuth.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch { /* logging must never hurt the game */ }
        }

        private static string Hex(byte[] b, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < b.Length && i < n; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
