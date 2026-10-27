// UBlade3 - exact HyperBlade (1996) simulation core.
// CollisionEllipsoid: src/physics/collisionellipsoid.cpp (0x0042e9b0..0x0042f350) and
// src/physics/collisionellipsoid.h. The arena and camera-tracker shells are the two globals
// src/engine/arenaellipsoidstatic.cpp (0x004ceff8) and src/engine/trackingellipsoidstatics.cpp
// (0x004cb3d0) construct.

using System;

namespace UBlade3.Sim
{
    // src/physics/collisionshape.h ShapeKind.
    public enum ShapeKind
    {
        Plane = 0,
        Ellipsoid = 2,
    }

    // src/physics/collisionellipsoid.h. The normal buffer matches the original's single m_vNormal
    // field: NormalAtPoint is not re-entrant and null only at the centre.
    public sealed class CollisionEllipsoid
    {
        // Compiled-in doubles from collisionellipsoid.cpp.
        private const double AxisEpsUpper = 0.0001;
        private const double AxisEpsLower = -0.0001;
        private const double AxisEps = 1e-05;
        private const float AxisEpsSquare = 1e-05f;
        private const float AxisBand = 0.0001f;
        private const double DirectionEps = 1e-08;

        public float RadiusX;
        public float RadiusY;
        public float RadiusZ;
        public float InvXX;
        public float InvYY;
        public float InvZZ;
        public float Friction;
        public float Elasticity;
        public ShapeKind ShapeKind;

        private Vec3 m_vNormal;
        private float m_flRatiosDivisor;
        private float m_flRatiosX;
        private float m_flRatiosY;
        private float m_flRatiosZ;

        // collisionellipsoid.cpp CollisionEllipsoid::CollisionEllipsoid (0x0042e9b0).
        public CollisionEllipsoid(float x, float y, float z, float friction, float elasticity)
        {
            RadiusX = x;
            RadiusY = y;
            RadiusZ = z;
            InvXX = 1.0f / (x * x);
            InvYY = 1.0f / (y * y);
            InvZZ = 1.0f / (z * z);
            Friction = friction;
            Elasticity = elasticity;
            ShapeKind = ShapeKind.Ellipsoid;
        }

        public float GetRadiusX() { return RadiusX; }
        public float GetRadiusY() { return RadiusY; }
        public float GetRadiusZ() { return RadiusZ; }
        public float GetInvXX() { return InvXX; }
        public float GetInvYY() { return InvYY; }
        public float GetInvZZ() { return InvZZ; }

        // collisionellipsoid.cpp PointInside (0x00408ea0): flSum = invZZ*z*z + invYY*y*y + invXX*x*x.
        public bool PointInside(Vec3 point)
        {
            float sum = InvZZ * point.z * point.z + InvYY * point.y * point.y + InvXX * point.x * point.x;
            return sum <= 1.0f;
        }

        // collisionellipsoid.cpp PointNearSurface (0x00408ee0): the same sum against 0.99.
        public bool PointNearSurface(Vec3 point)
        {
            float sum = InvZZ * point.z * point.z + InvYY * point.y * point.y + InvXX * point.x * point.x;
            return sum <= 0.99f;
        }

        // collisionellipsoid.cpp GetZ (0x0042f0d0): a point outside the ellipse answers one.
        public float GetZ(float x, float y)
        {
            float remainder = (1.0f - x * InvXX * x) - y * InvYY * y;

            if (remainder < 0.0f)
            {
                return 1.0f;
            }

            return -((float)Math.Sqrt(remainder) * RadiusZ);
        }

        // collisionellipsoid.cpp SegmentIntersection (0x0042ea30): the ray origin is the second point.
        public int SegmentIntersection(Vec3 first, Vec3 second, out Vec3 nearOut)
        {
            Vec3 direction = new Vec3(second.x - first.x, second.y - first.y, second.z - first.z);
            Vec3 farUnused;
            return IntersectRay(direction, second, out nearOut, out farUnused);
        }

        // collisionellipsoid.cpp IntersectSegment (0x0042ea80).
        public int IntersectSegment(Vec3 first, Vec3 second, out Vec3 nearOut, out Vec3 farOut)
        {
            Vec3 direction = new Vec3(second.x - first.x, second.y - first.y, second.z - first.z);
            return IntersectRay(direction, second, out nearOut, out farOut);
        }

        // collisionellipsoid.cpp RayIntersection (0x0042ead0): the far point goes into a scratch.
        public int RayIntersection(Vec3 direction, Vec3 origin, out Vec3 pointOut)
        {
            Vec3 farUnused;
            return IntersectRay(direction, origin, out pointOut, out farUnused);
        }

        // collisionellipsoid.cpp IntersectRay (0x0042eb00). Parameterised by the first safe
        // direction component (not the largest) and solved in double, as in the original.
        public int IntersectRay(Vec3 direction, Vec3 origin, out Vec3 nearOut, out Vec3 farOut)
        {
            nearOut = Vec3.Zero;
            farOut = Vec3.Zero;

            double slopeA;
            double slopeB;
            double quad;
            double halfB;
            double armA;
            double armB;
            double root;
            double rootLo;
            double rootHi;
            double offsetLo;
            double offsetHi;

            double originX = origin.x;
            double originY = origin.y;
            double originZ = origin.z;

            double dirX = direction.x;
            double dirY = direction.y;
            double dirZ = direction.z;

            if (dirX > AxisEpsUpper || dirX < AxisEpsLower)
            {
                slopeA = dirY / dirX;
                slopeB = dirZ / dirX;

                quad = (InvYY * (slopeA * slopeA)) + InvZZ * (slopeB * slopeB) + InvXX;

                halfB = (InvYY * (slopeA * originY - slopeA * slopeA * originX)
                         + (slopeB * originZ - slopeB * slopeB * originX) * InvZZ) / quad;

                armA = slopeA * originX - originY;
                armB = slopeB * originX - originZ;

                root = halfB * halfB - ((InvZZ * armB * armB + InvYY * armA * armA) - 1.0) / quad;

                if (0.0 <= root)
                {
                    root = Math.Sqrt(root);
                }
                else if (root > AxisEpsLower)
                {
                    root = 0.0;
                }
                else
                {
                    return 0;
                }

                rootLo = -halfB - root;
                rootHi = root - halfB;

                offsetLo = rootLo - originX;
                offsetHi = rootHi - originX;

                if (Math.Abs(offsetLo) <= Math.Abs(offsetHi))
                {
                    nearOut = new Vec3((float)rootLo, (float)(offsetLo * slopeA + originY), (float)(offsetLo * slopeB + originZ));
                    farOut = new Vec3((float)rootHi, (float)(offsetHi * slopeA + originY), (float)(offsetHi * slopeB + originZ));
                }
                else
                {
                    nearOut = new Vec3((float)rootHi, (float)(offsetHi * slopeA + originY), (float)(offsetHi * slopeB + originZ));
                    farOut = new Vec3((float)rootLo, (float)(offsetLo * slopeA + originY), (float)(offsetLo * slopeB + originZ));
                }

                // The original calls Vec3::FindClosest(pOrigin, pNearOut, pFarOut) and discards
                // the answer; the call has no effect on its arguments and is kept only as a note.
                return 1;
            }

            if (dirY <= AxisEpsUpper && dirY >= AxisEpsLower)
            {
                slopeA = dirY / dirZ;
                slopeB = dirX / dirZ;

                quad = (InvYY * (slopeA * slopeA)) + InvXX * (slopeB * slopeB) + InvZZ;

                halfB = ((slopeB * originX - slopeB * slopeB * originZ) * InvXX
                         + (InvYY * (slopeA * originY - slopeA * slopeA * originZ))) / quad;

                armA = slopeA * originZ - originY;
                armB = slopeB * originZ - originX;

                root = halfB * halfB - ((InvXX * armB * armB + (InvYY * armA * armA)) - 1.0) / quad;

                if (0.0 <= root)
                {
                    root = Math.Sqrt(root);
                }
                else if (root > AxisEpsLower)
                {
                    root = 0.0;
                }
                else
                {
                    return 0;
                }

                rootLo = -halfB - root;
                rootHi = root - halfB;

                offsetLo = rootLo - originZ;
                offsetHi = rootHi - originZ;

                if (Math.Abs(offsetLo) <= Math.Abs(offsetHi))
                {
                    nearOut = new Vec3((float)(slopeB * offsetLo + originX), (float)(offsetLo * slopeA + originY), (float)rootLo);
                    farOut = new Vec3((float)(slopeB * offsetHi + originX), (float)(offsetHi * slopeA + originY), (float)rootHi);
                }
                else
                {
                    nearOut = new Vec3((float)(slopeB * offsetHi + originX), (float)(offsetHi * slopeA + originY), (float)rootHi);
                    farOut = new Vec3((float)(slopeB * offsetLo + originX), (float)(offsetLo * slopeA + originY), (float)rootLo);
                }

                return 1;
            }

            slopeB = dirX / dirY;
            slopeA = dirZ / dirY;

            quad = InvXX * (slopeB * slopeB) + InvZZ * (slopeA * slopeA) + InvYY;

            halfB = ((slopeB * originX - slopeB * slopeB * originY) * InvXX
                     + (InvZZ * (slopeA * originZ - slopeA * slopeA * originY))) / quad;

            armB = slopeB * originY - originX;
            armA = slopeA * originY - originZ;

            root = halfB * halfB - ((InvZZ * armA * armA + InvXX * armB * armB) - 1.0) / quad;

            if (0.0 <= root)
            {
                root = Math.Sqrt(root);
            }
            else if (root > AxisEpsLower)
            {
                root = 0.0;
            }
            else
            {
                return 0;
            }

            rootLo = -halfB - root;
            rootHi = root - halfB;

            offsetLo = rootLo - originY;
            offsetHi = rootHi - originY;

            if (Math.Abs(offsetLo) <= Math.Abs(offsetHi))
            {
                nearOut = new Vec3((float)(slopeB * offsetLo + originX), (float)rootLo, (float)(offsetLo * slopeA + originZ));
                farOut = new Vec3((float)(slopeB * offsetHi + originX), (float)rootHi, (float)(offsetHi * slopeA + originZ));
            }
            else
            {
                nearOut = new Vec3((float)(slopeB * offsetHi + originX), (float)rootHi, (float)(offsetHi * slopeA + originZ));
                farOut = new Vec3((float)(slopeB * offsetLo + originX), (float)rootLo, (float)(offsetLo * slopeA + originZ));
            }

            return 1;
        }

        // collisionellipsoid.cpp ComputeRadialBoundaryXY (0x0042f040). Wrong near the axis: with
        // |x| under 1e-5 it answers about (-a, 0, z) instead of (0, +-b, z); shipped behaviour kept.
        public void ComputeRadialBoundaryXY(Vec3 input, out Vec3 output)
        {
            bool offAxis = true;
            float square;
            float radius;

            if (Math.Abs((double)input.x) > AxisEps)
            {
                square = input.x * input.x;
            }
            else
            {
                square = AxisEpsSquare;
                offAxis = false;
            }

            radius = (float)Math.Sqrt(square / (square * InvXX + InvYY * input.y * input.y));

            if (input.x <= 0.0f)
            {
                radius = -radius;
            }

            output.x = radius;

            if (offAxis)
            {
                output.y = (input.y / input.x) * radius;
                output.z = input.z;
                return;
            }

            output.y = 0.0f;
            output.z = input.z;
        }

        // collisionellipsoid.cpp NormalAtPoint (0x0042f120). Null only at the centre; the returned
        // vector lives in the instance, as the original's m_vNormal does.
        public bool NormalAtPoint(Vec3 point, out Vec3 normal)
        {
            if (point.z <= AxisBand && point.z >= -AxisBand)
            {
                if (point.x <= AxisBand && point.x >= -AxisBand)
                {
                    if (point.y <= AxisBand && point.y >= -AxisBand)
                    {
                        normal = Vec3.Zero;
                        return false;
                    }

                    float axis = RadiusY;
                    m_flRatiosDivisor = axis * axis / point.y;
                    m_flRatiosZ = m_flRatiosDivisor * InvZZ * point.z;
                    m_flRatiosX = InvXX * point.x * m_flRatiosDivisor;

                    if (point.y > 0.0f)
                    {
                        m_vNormal.y = -(1.0f / (float)Math.Sqrt(m_flRatiosZ * m_flRatiosZ + m_flRatiosX * m_flRatiosX + 1.0f));
                    }
                    else
                    {
                        m_vNormal.y = 1.0f / (float)Math.Sqrt(m_flRatiosZ * m_flRatiosZ + m_flRatiosX * m_flRatiosX + 1.0f);
                    }

                    m_vNormal.x = m_flRatiosX * m_vNormal.y;
                    m_vNormal.z = m_flRatiosZ * m_vNormal.y;
                    normal = m_vNormal;
                    return true;
                }

                {
                    float axis = RadiusX;
                    m_flRatiosDivisor = axis * axis / point.x;
                    m_flRatiosY = InvYY * point.y * m_flRatiosDivisor;
                    m_flRatiosZ = m_flRatiosDivisor * InvZZ * point.z;

                    if (point.x > 0.0f)
                    {
                        m_vNormal.x = -(1.0f / (float)Math.Sqrt(m_flRatiosZ * m_flRatiosZ + m_flRatiosY * m_flRatiosY + 1.0f));
                    }
                    else
                    {
                        m_vNormal.x = 1.0f / (float)Math.Sqrt(m_flRatiosZ * m_flRatiosZ + m_flRatiosY * m_flRatiosY + 1.0f);
                    }

                    m_vNormal.z = m_flRatiosZ * m_vNormal.x;
                    m_vNormal.y = m_flRatiosY * m_vNormal.x;
                    normal = m_vNormal;
                    return true;
                }
            }

            {
                float axis = RadiusZ;
                m_flRatiosDivisor = axis * axis / point.z;
                m_flRatiosY = InvYY * point.y * m_flRatiosDivisor;
                m_flRatiosX = InvXX * point.x * m_flRatiosDivisor;

                if (point.z > 0.0f)
                {
                    m_vNormal.z = -(1.0f / (float)Math.Sqrt(m_flRatiosX * m_flRatiosX + m_flRatiosY * m_flRatiosY + 1.0f));
                }
                else
                {
                    m_vNormal.z = 1.0f / (float)Math.Sqrt(m_flRatiosX * m_flRatiosX + m_flRatiosY * m_flRatiosY + 1.0f);
                }

                m_vNormal.x = m_flRatiosX * m_vNormal.z;
                m_vNormal.y = m_flRatiosY * m_vNormal.z;
                normal = m_vNormal;
                return true;
            }
        }

        // collisionellipsoid.cpp RadiusForDirection (0x0042f350): /3 is a tuning constant and the
        // third semi-axis is never read.
        public float RadiusForDirection(Vec3 direction)
        {
            float horizontal = (float)((Math.Abs((double)direction.x) + Math.Abs((double)direction.z)) / 2.0f);
            float absY = (float)Math.Abs((double)direction.y);
            float total = absY + horizontal;

            if (total < DirectionEps)
            {
                return 0.0f;
            }

            return (horizontal * RadiusX + absY * RadiusY) / (3.0f * total);
        }
    }

    // src/engine/arenaellipsoidstatic.cpp (0x004ceff8) and trackingellipsoidstatics.cpp (0x004cb3d0).
    public static class EllipsoidStatics
    {
        // g_arenaEllipsoid: 70/125/70 * g_flTrackingEllipsoidScale, friction 0.01, elasticity 0.95.
        public static readonly CollisionEllipsoid Arena = new CollisionEllipsoid(
            ArenaConstants.RadiusX,
            ArenaConstants.RadiusY,
            ArenaConstants.RadiusZ,
            ArenaConstants.Friction,
            ArenaConstants.Elasticity);

        // g_camTrackEllipsoid: 2.2 inside the arena's, friction 0.01, elasticity 0.5.
        public static readonly CollisionEllipsoid CamTrack = new CollisionEllipsoid(
            ArenaConstants.CamTrackRadiusX,
            ArenaConstants.CamTrackRadiusY,
            ArenaConstants.CamTrackRadiusZ,
            ArenaConstants.CamTrackFriction,
            ArenaConstants.CamTrackElasticity);
    }
}
