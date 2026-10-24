using System;
using System.Linq;

namespace ProCodex;

// A bounded, typed global permutation test. No model-supplied expressions execute here.
public static class SpatialPermutation {
    public sealed record Observation(long Id,double X,double Y,double Value);
    public sealed record Result(double MoranI,double ExpectedI,double PermutationP,int Permutations,int Seed,int Neighbors,int Count,string Weights,string Alternative);
    public static Result Global(Observation[] observations,int neighbors,int seed,int permutations=999) {
        if(observations.Length<4||observations.Length>5000||neighbors<1||neighbors>=observations.Length||permutations<99||permutations>9999)throw new ArgumentException("置换检验需4–5000个有效单元，近邻数小于单元数，置换99–9999次。");
        if(observations.Select(o=>o.Id).Distinct().Count()!=observations.Length||observations.Any(o=>!double.IsFinite(o.X)||!double.IsFinite(o.Y)||!double.IsFinite(o.Value)))throw new ArgumentException("观测ID须唯一，坐标和值必须有限且无缺失。");
        var data=observations.OrderBy(o=>o.Id).ToArray();double mean=data.Average(o=>o.Value);var z=data.Select(o=>o.Value-mean).ToArray();double denominator=z.Sum(v=>v*v);
        if(!double.IsFinite(denominator)||denominator<=0)throw new ArgumentException("分析字段必须存在有限的非零方差。");
        var weights=Enumerable.Range(0,data.Length).Select(i=>Enumerable.Range(0,data.Length).Where(j=>j!=i).OrderBy(j=>(data[i].X-data[j].X)*(data[i].X-data[j].X)+(data[i].Y-data[j].Y)*(data[i].Y-data[j].Y)).ThenBy(j=>data[j].Id).Take(neighbors).ToArray()).ToArray();
        double Index(double[] values){double numerator=0;for(int i=0;i<values.Length;i++)numerator+=values[i]*weights[i].Sum(j=>values[j])/neighbors;return numerator/denominator;}
        double observed=Index(z),expected=-1.0/(data.Length-1),distance=Math.Abs(observed-expected);int extreme=0;var random=new Random(seed);
        for(int iteration=0;iteration<permutations;iteration++) {var shuffled=(double[])z.Clone();for(int i=shuffled.Length-1;i>0;i--){int j=random.Next(i+1);(shuffled[i],shuffled[j])=(shuffled[j],shuffled[i]);}if(Math.Abs(Index(shuffled)-expected)+1e-12>=distance)extreme++;}
        return new(observed,expected,(extreme+1.0)/(permutations+1),permutations,seed,neighbors,data.Length,"Euclidean K-nearest; directed; row-standardized; distance ties by ascending object ID","two-sided distance from expected I; fixed weights; shuffled values");
    }
}
