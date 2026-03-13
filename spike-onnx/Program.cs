using System;
using System.IO;
using System.Linq;

// Step 0: Generate a minimal ONNX model (Add two floats)
// This avoids needing to download anything.
// The model: input_a (float[1]) + input_b (float[1]) = output (float[1])
var modelBytes = GenerateAddModel();
var modelPath = Path.Combine(AppContext.BaseDirectory, "test_add.onnx");
File.WriteAllBytes(modelPath, modelBytes);
Console.Error.WriteLine($"[spike] Model written: {modelPath} ({modelBytes.Length} bytes)");

// Step 1: Load ONNX Runtime + create session
Console.Error.WriteLine("[spike] Creating InferenceSession...");
Microsoft.ML.OnnxRuntime.InferenceSession? session = null;
try
{
    session = new Microsoft.ML.OnnxRuntime.InferenceSession(modelPath);
    Console.Error.WriteLine("[spike] Session created OK");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[spike] FAIL: Session creation crashed: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("RESULT: FAIL_SESSION");
    return 1;
}

// Step 2: Run inference
Console.Error.WriteLine("[spike] Running inference (3.0 + 4.0)...");
try
{
    var inputA = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(new float[] { 3.0f }, new[] { 1 });
    var inputB = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(new float[] { 4.0f }, new[] { 1 });
    var inputs = new[]
    {
        Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("A", inputA),
        Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor("B", inputB)
    };

    using var results = session.Run(inputs);
    var output = results.First().AsTensor<float>();
    var value = output[0];

    Console.Error.WriteLine($"[spike] Inference result: {value}");
    if (Math.Abs(value - 7.0f) < 0.001f)
    {
        Console.Error.WriteLine("[spike] Result correct: 3 + 4 = 7");
        Console.WriteLine("RESULT: PASS");
        session.Dispose();
        return 0;
    }
    else
    {
        Console.Error.WriteLine($"[spike] FAIL: Expected 7.0, got {value}");
        Console.WriteLine("RESULT: FAIL_WRONG_RESULT");
        session.Dispose();
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[spike] FAIL: Inference crashed: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    Console.WriteLine("RESULT: FAIL_INFERENCE");
    session?.Dispose();
    return 1;
}

// ── Minimal ONNX model generator ──
// Builds a valid ONNX protobuf for: output = A + B
// Hand-assembled protobuf bytes — no protobuf library needed.
static byte[] GenerateAddModel()
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);

    // Helper: write protobuf varint
    void WriteVarint(ulong v)
    {
        while (v >= 0x80) { w.Write((byte)(v | 0x80)); v >>= 7; }
        w.Write((byte)v);
    }

    // Helper: write protobuf field tag
    void WriteTag(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

    // Helper: write length-delimited field
    void WriteLenField(int field, byte[] data)
    {
        WriteTag(field, 2);
        WriteVarint((ulong)data.Length);
        w.Write(data);
    }

    // Helper: write string field
    void WriteStringField(int field, string s) => WriteLenField(field, System.Text.Encoding.UTF8.GetBytes(s));

    // Helper: write varint field
    void WriteVarintField(int field, long v)
    {
        WriteTag(field, 0);
        WriteVarint((ulong)v);
    }

    // Build innermost structures first, then wrap them

    // TensorTypeProto for float[1]: elem_type=1(FLOAT), shape={dim:[{dim_value:1}]}
    byte[] MakeTensorType()
    {
        using var ts = new MemoryStream();
        using var tw = new BinaryWriter(ts);
        void TV(ulong v2) { while (v2 >= 0x80) { tw.Write((byte)(v2 | 0x80)); v2 >>= 7; } tw.Write((byte)v2); }
        void TTag(int f2, int wt2) => TV((ulong)((f2 << 3) | wt2));
        void TLenField(int f2, byte[] d2) { TTag(f2, 2); TV((ulong)d2.Length); tw.Write(d2); }
        void TVarintField(int f2, long v2) { TTag(f2, 0); TV((ulong)v2); }

        // elem_type = 1 (FLOAT)
        TVarintField(1, 1);

        // shape (field 2) = TensorShapeProto { dim: [{ dim_value: 1 }] }
        // Dimension: dim_value (field 1) = 1
        byte[] dim;
        using (var ds = new MemoryStream())
        {
            using var dw = new BinaryWriter(ds);
            void DV(ulong v3) { while (v3 >= 0x80) { dw.Write((byte)(v3 | 0x80)); v3 >>= 7; } dw.Write((byte)v3); }
            void DTag(int f3, int wt3) => DV((ulong)((f3 << 3) | wt3));
            DTag(1, 0); DV(1); // dim_value = 1
            dim = ds.ToArray();
        }

        // Shape: dim (field 1, repeated)
        byte[] shape;
        using (var ss = new MemoryStream())
        {
            using var sw = new BinaryWriter(ss);
            void SV(ulong v4) { while (v4 >= 0x80) { sw.Write((byte)(v4 | 0x80)); v4 >>= 7; } sw.Write((byte)v4); }
            void STag(int f4, int wt4) => SV((ulong)((f4 << 3) | wt4));
            STag(1, 2); SV((ulong)dim.Length); sw.Write(dim);
            shape = ss.ToArray();
        }

        TLenField(2, shape);
        return ts.ToArray();
    }

    // TypeProto: tensor_type (field 1)
    byte[] MakeTypeProto()
    {
        var tt = MakeTensorType();
        using var ts2 = new MemoryStream();
        using var tw2 = new BinaryWriter(ts2);
        void TV2(ulong v) { while (v >= 0x80) { tw2.Write((byte)(v | 0x80)); v >>= 7; } tw2.Write((byte)v); }
        void TTag2(int f, int wt) => TV2((ulong)((f << 3) | wt));
        TTag2(1, 2); TV2((ulong)tt.Length); tw2.Write(tt);
        return ts2.ToArray();
    }

    // ValueInfoProto: name (field 1, string), type (field 2, TypeProto)
    byte[] MakeValueInfo(string name)
    {
        var tp = MakeTypeProto();
        using var vs = new MemoryStream();
        using var vw = new BinaryWriter(vs);
        void VV(ulong v) { while (v >= 0x80) { vw.Write((byte)(v | 0x80)); v >>= 7; } vw.Write((byte)v); }
        void VTag(int f, int wt) => VV((ulong)((f << 3) | wt));
        void VLenField(int f, byte[] d) { VTag(f, 2); VV((ulong)d.Length); vw.Write(d); }
        VLenField(1, System.Text.Encoding.UTF8.GetBytes(name));
        VLenField(2, tp);
        return vs.ToArray();
    }

    // NodeProto: Add node
    byte[] MakeAddNode()
    {
        using var ns = new MemoryStream();
        using var nw = new BinaryWriter(ns);
        void NV(ulong v) { while (v >= 0x80) { nw.Write((byte)(v | 0x80)); v >>= 7; } nw.Write((byte)v); }
        void NTag(int f, int wt) => NV((ulong)((f << 3) | wt));
        void NLenField(int f, byte[] d) { NTag(f, 2); NV((ulong)d.Length); nw.Write(d); }
        var enc = System.Text.Encoding.UTF8;
        NLenField(1, enc.GetBytes("A"));       // input[0]
        NLenField(1, enc.GetBytes("B"));       // input[1]
        NLenField(2, enc.GetBytes("output"));  // output[0]
        NLenField(4, enc.GetBytes("Add"));     // op_type
        return ns.ToArray();
    }

    // GraphProto
    byte[] MakeGraph()
    {
        var addNode = MakeAddNode();
        var viA = MakeValueInfo("A");
        var viB = MakeValueInfo("B");
        var viOut = MakeValueInfo("output");

        using var gs = new MemoryStream();
        using var gw = new BinaryWriter(gs);
        void GV(ulong v) { while (v >= 0x80) { gw.Write((byte)(v | 0x80)); v >>= 7; } gw.Write((byte)v); }
        void GTag(int f, int wt) => GV((ulong)((f << 3) | wt));
        void GLenField(int f, byte[] d) { GTag(f, 2); GV((ulong)d.Length); gw.Write(d); }

        GLenField(1, addNode);                                          // node (field 1)
        GLenField(2, System.Text.Encoding.UTF8.GetBytes("test_graph")); // name (field 2)
        GLenField(11, viA);                                             // input (field 11)
        GLenField(11, viB);
        GLenField(12, viOut);                                           // output (field 12)

        return gs.ToArray();
    }

    // ModelProto
    WriteVarintField(1, 9);                  // ir_version = 9
    WriteVarintField(5, 21);                 // opset_import version placeholder
    WriteStringField(2, "spike");            // producer_name
    WriteLenField(7, MakeGraph());           // graph (field 7)

    // opset_import (field 8): OperatorSetIdProto { version: 21 }
    byte[] opset;
    using (var os = new MemoryStream())
    {
        using var ow = new BinaryWriter(os);
        void OV(ulong v) { while (v >= 0x80) { ow.Write((byte)(v | 0x80)); v >>= 7; } ow.Write((byte)v); }
        void OTag(int f, int wt) => OV((ulong)((f << 3) | wt));
        OTag(2, 0); OV(21); // version = 21
        opset = os.ToArray();
    }
    WriteLenField(8, opset);

    return ms.ToArray();
}
