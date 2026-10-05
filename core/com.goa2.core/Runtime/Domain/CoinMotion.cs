using System;
namespace Goa2.Domain
{
    // Transient host-authored presentation frame; never a rule input or private projection.
    [Serializable]
    public sealed class CoinMotion
    {
        public string TossId="";
        public long Sequence;
        public float Time;
        public float[] Position=new float[3], Rotation=new float[]{0,0,0,1}, Velocity=new float[3], AngularVelocity=new float[3];
    }
}
