namespace ClaimWise.Rag.Embeddings;

public static class VectorMath
{
    public static void NormalizeInPlace(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += (double)x * x;
        if (sum == 0) return;
        var inv = (float)(1.0 / Math.Sqrt(sum));
        for (int i = 0; i < v.Length; i++) v[i] *= inv;
    }

    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Serialise a vector to little-endian bytes for storage in a BLOB/bytea column.</summary>
    public static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var v = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, v, 0, v.Length * sizeof(float));
        return v;
    }
}
