using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace GestionSite.Modules.Patrimoine.RenduPdf;

/// <summary>
/// PNG minimal (RVB 8 bits, sans filtre, un seul bloc de données) produit depuis l'image BGR de PDFium, compressé ligne à
/// ligne : seule l'image compressée est gardée en mémoire. L'API le décode ensuite comme une image importée (S21-04).
/// </summary>
internal sealed class Png : IDisposable
{
    private static readonly uint[] TableCrc = CreerTable();
    private readonly MemoryStream _donnees;
    private readonly byte[] _entete;

    private Png(MemoryStream donnees, byte[] entete)
    {
        _donnees = donnees;
        _entete = entete;
    }

    /// <summary>Taille totale du fichier PNG, en octets.</summary>
    internal long Length => 8 + (12 + 13) + (12 + _donnees.Length) + 12;

    internal static Png Encoder(IntPtr bgr, int largeur, int hauteur, int pas)
    {
        var entete = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(entete, largeur);
        BinaryPrimitives.WriteInt32BigEndian(entete.AsSpan(4), hauteur);
        entete[8] = 8; // bits par composante
        entete[9] = 2; // RVB, sans transparence

        var donnees = new MemoryStream();
        var ligne = new byte[1 + (3 * largeur)]; // octet de filtre (0 : aucun) puis les pixels
        using (var compression = new ZLibStream(donnees, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < hauteur; y++)
            {
                Marshal.Copy(bgr + (y * (nint)pas), ligne, 1, 3 * largeur);
                for (var x = 1; x < ligne.Length; x += 3)
                {
                    (ligne[x], ligne[x + 2]) = (ligne[x + 2], ligne[x]);
                }

                compression.Write(ligne);
            }
        }

        return new Png(donnees, entete);
    }

    internal void EcrireVers(Stream sortie)
    {
        sortie.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Bloc(sortie, "IHDR"u8, _entete);
        Bloc(sortie, "IDAT"u8, _donnees.GetBuffer().AsSpan(0, (int)_donnees.Length));
        Bloc(sortie, "IEND"u8, []);
    }

    public void Dispose() => _donnees.Dispose();

    private static void Bloc(Stream sortie, ReadOnlySpan<byte> type, ReadOnlySpan<byte> contenu)
    {
        Span<byte> quatre = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(quatre, contenu.Length);
        sortie.Write(quatre);
        sortie.Write(type);
        sortie.Write(contenu);
        BinaryPrimitives.WriteUInt32BigEndian(quatre, ~Crc(Crc(uint.MaxValue, type), contenu));
        sortie.Write(quatre);
    }

    // CRC-32 de la norme PNG (polynôme 0xEDB88320).
    private static uint Crc(uint crc, ReadOnlySpan<byte> octets)
    {
        foreach (var octet in octets)
        {
            crc = TableCrc[(crc ^ octet) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreerTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
