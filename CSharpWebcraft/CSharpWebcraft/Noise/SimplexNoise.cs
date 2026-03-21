namespace CSharpWebcraft.Noise;

public class SimplexNoise
{
    private readonly short[] _perm = new short[512];
    private readonly short[] _permMod12 = new short[512];

    private static readonly double[] Grad3 = {
        1,1,0, -1,1,0, 1,-1,0, -1,-1,0,
        1,0,1, -1,0,1, 1,0,-1, -1,0,-1,
        0,1,1, 0,-1,1, 0,1,-1, 0,-1,-1
    };

    private const double F2 = 0.5 * (1.7320508075688772 - 1.0);
    private const double G2 = (3.0 - 1.7320508075688772) / 6.0;
    private const double F3 = 1.0 / 3.0;
    private const double G3 = 1.0 / 6.0;

    public SimplexNoise(int? seed = null)
    {
        var p = new short[256];
        for (int i = 0; i < 256; i++) p[i] = (short)i;
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++)
        {
            _perm[i] = p[i & 255];
            _permMod12[i] = (short)(_perm[i] % 12);
        }
    }

    public double Noise2D(double x, double y)
    {
        double s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);
        double t = (i + j) * G2;
        double x0 = x - (i - t);
        double y0 = y - (j - t);
        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; } else { i1 = 0; j1 = 1; }
        double x1 = x0 - i1 + G2;
        double y1 = y0 - j1 + G2;
        double x2 = x0 - 1.0 + 2.0 * G2;
        double y2 = y0 - 1.0 + 2.0 * G2;
        int ii = i & 255, jj = j & 255;
        int gi0 = _permMod12[ii + _perm[jj]];
        int gi1 = _permMod12[ii + i1 + _perm[jj + j1]];
        int gi2 = _permMod12[ii + 1 + _perm[jj + 1]];
        double n0 = 0, n1 = 0, n2 = 0;
        double t0 = 0.5 - x0 * x0 - y0 * y0;
        if (t0 >= 0) { t0 *= t0; n0 = t0 * t0 * Dot2(gi0, x0, y0); }
        double t1 = 0.5 - x1 * x1 - y1 * y1;
        if (t1 >= 0) { t1 *= t1; n1 = t1 * t1 * Dot2(gi1, x1, y1); }
        double t2 = 0.5 - x2 * x2 - y2 * y2;
        if (t2 >= 0) { t2 *= t2; n2 = t2 * t2 * Dot2(gi2, x2, y2); }
        return 70.0 * (n0 + n1 + n2);
    }

    public double Noise3D(double x, double y, double z)
    {
        double s = (x + y + z) * F3;
        int i = FastFloor(x + s), j = FastFloor(y + s), k = FastFloor(z + s);
        double t = (i + j + k) * G3;
        double x0 = x - (i - t), y0 = y - (j - t), z0 = z - (k - t);
        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0) { i1=1;j1=0;k1=0;i2=1;j2=1;k2=0; }
            else if (x0 >= z0) { i1=1;j1=0;k1=0;i2=1;j2=0;k2=1; }
            else { i1=0;j1=0;k1=1;i2=1;j2=0;k2=1; }
        }
        else
        {
            if (y0 < z0) { i1=0;j1=0;k1=1;i2=0;j2=1;k2=1; }
            else if (x0 < z0) { i1=0;j1=1;k1=0;i2=0;j2=1;k2=1; }
            else { i1=0;j1=1;k1=0;i2=1;j2=1;k2=0; }
        }
        double x1=x0-i1+G3, y1=y0-j1+G3, z1=z0-k1+G3;
        double x2=x0-i2+2*G3, y2=y0-j2+2*G3, z2=z0-k2+2*G3;
        double x3=x0-1+3*G3, y3=y0-1+3*G3, z3=z0-1+3*G3;
        int ii=i&255, jj=j&255, kk=k&255;
        int gi0=_permMod12[ii+_perm[jj+_perm[kk]]];
        int gi1=_permMod12[ii+i1+_perm[jj+j1+_perm[kk+k1]]];
        int gi2=_permMod12[ii+i2+_perm[jj+j2+_perm[kk+k2]]];
        int gi3=_permMod12[ii+1+_perm[jj+1+_perm[kk+1]]];
        double n0=0, n1=0, n2=0, n3=0;
        double t0b=0.6-x0*x0-y0*y0-z0*z0;
        if (t0b >= 0) { t0b*=t0b; n0=t0b*t0b*Dot3(gi0,x0,y0,z0); }
        double t1b=0.6-x1*x1-y1*y1-z1*z1;
        if (t1b >= 0) { t1b*=t1b; n1=t1b*t1b*Dot3(gi1,x1,y1,z1); }
        double t2b=0.6-x2*x2-y2*y2-z2*z2;
        if (t2b >= 0) { t2b*=t2b; n2=t2b*t2b*Dot3(gi2,x2,y2,z2); }
        double t3=0.6-x3*x3-y3*y3-z3*z3;
        if (t3 >= 0) { t3*=t3; n3=t3*t3*Dot3(gi3,x3,y3,z3); }
        return 32.0 * (n0+n1+n2+n3);
    }

    private static int FastFloor(double x) { int xi = (int)x; return x < xi ? xi - 1 : xi; }
    private static double Dot2(int gi, double x, double y) { int idx = gi * 3; return Grad3[idx]*x + Grad3[idx+1]*y; }
    private static double Dot3(int gi, double x, double y, double z) { int idx = gi * 3; return Grad3[idx]*x + Grad3[idx+1]*y + Grad3[idx+2]*z; }
}
