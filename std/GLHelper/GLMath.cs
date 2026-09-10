namespace GLHelper;

public static partial class GL
{
    /// <summary>
    /// Sets up a perspective projection matrix matching gluPerspective.
    /// Multiplies the current matrix (usually GL_PROJECTION).
    /// </summary>
    public static void Perspective(double fovyDegrees, double aspect, double zNear, double zFar)
    {
        if (aspect <= 0.0 || zNear <= 0.0 || zFar <= zNear) return;

        double ymax = zNear * Math.Tan(fovyDegrees * Math.PI / 360.0);
        double ymin = -ymax;
        double xmin = ymin * aspect;
        double xmax = ymax * aspect;

        Frustum(xmin, xmax, ymin, ymax, zNear, zFar);
    }

    /// <summary>
    /// Sets up a viewing matrix matching gluLookAt.
    /// Multiplies the current matrix (usually GL_MODELVIEW).
    /// </summary>
    public static void LookAt(
        double eyeX, double eyeY, double eyeZ,
        double centerX, double centerY, double centerZ,
        double upX, double upY, double upZ)
    {
        // Forward vector = normalize(center - eye)
        double fx = centerX - eyeX;
        double fy = centerY - eyeY;
        double fz = centerZ - eyeZ;
        double flen = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        if (flen > 1e-9)
        {
            fx /= flen; fy /= flen; fz /= flen;
        }

        // Up vector normalize
        double ulen = Math.Sqrt(upX * upX + upY * upY + upZ * upZ);
        if (ulen > 1e-9)
        {
            upX /= ulen; upY /= ulen; upZ /= ulen;
        }

        // Side vector = forward x up
        double sx = fy * upZ - fz * upY;
        double sy = fz * upX - fx * upZ;
        double sz = fx * upY - fy * upX;
        double slen = Math.Sqrt(sx * sx + sy * sy + sz * sz);
        if (slen > 1e-9)
        {
            sx /= slen; sy /= slen; sz /= slen;
        }

        // Recalculate up = side x forward
        double ux = sy * fz - sz * fy;
        double uy = sz * fx - sx * fz;
        double uz = sx * fy - sy * fx;

        // Build 4x4 matrix in column-major order
        float[] m = new float[16];
        m[0] = (float)sx;  m[4] = (float)sy;  m[8]  = (float)sz;  m[12] = 0.0f;
        m[1] = (float)ux;  m[5] = (float)uy;  m[9]  = (float)uz;  m[13] = 0.0f;
        m[2] = (float)-fx; m[6] = (float)-fy; m[10] = (float)-fz; m[14] = 0.0f;
        m[3] = 0.0f;       m[7] = 0.0f;       m[11] = 0.0f;       m[15] = 1.0f;

        MultMatrix(m);
        Translatef((float)-eyeX, (float)-eyeY, (float)-eyeZ);
    }

    /// <summary>Creates a 16-element identity matrix in column-major order.</summary>
    public static float[] CreateIdentityMatrix()
    {
        return
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f
        ];
    }

    /// <summary>Creates a 4x4 perspective projection matrix for shaders.</summary>
    public static float[] CreatePerspectiveMatrix(double fovyDegrees, double aspect, double zNear, double zFar)
    {
        float[] m = new float[16];
        double f = 1.0 / Math.Tan(fovyDegrees * Math.PI / 360.0);

        m[0] = (float)(f / aspect);
        m[5] = (float)f;
        m[10] = (float)((zFar + zNear) / (zNear - zFar));
        m[11] = -1.0f;
        m[14] = (float)((2.0 * zFar * zNear) / (zNear - zFar));

        return m;
    }

    /// <summary>Multiplies two 4x4 column-major matrices: result = a * b.</summary>
    public static float[] MultiplyMatrices(float[] a, float[] b)
    {
        float[] result = new float[16];
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                float sum = 0f;
                for (int i = 0; i < 4; i++)
                {
                    sum += a[row + i * 4] * b[i + col * 4];
                }
                result[row + col * 4] = sum;
            }
        }
        return result;
    }
}
