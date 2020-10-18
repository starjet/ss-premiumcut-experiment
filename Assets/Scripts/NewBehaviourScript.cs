using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Spine.Unity;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UTJ.FrameCapturer;
using ImageMagick;

namespace Spine3.Unity
{
    public class Spine3SkeletonDataAsset : SkeletonDataAsset { }
    public class Spine3AtlasAsset : AtlasAsset { }
}

public class NewBehaviourScript : MonoBehaviour
{
    SkeletonAnimation skeletonAnimation = null;
    SkeletonAnimation bgSkeletonAnimation = null;
    SkeletonAnimation fgSkeletonAnimation = null;
    RenderTexture mRenderTexture;
    Stack<int> xStack = new Stack<int>();
    Stack<int> yStack = new Stack<int>();
    Stack<int> widthStack = new Stack<int>();
    Stack<int> heightStack = new Stack<int>();

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    void Awake()
    {
        try
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(@"N:\unity_premium_cut\1\card_cartoon_100669.unity3d.lz4.extracted"); //Path to uncompressed assetbundle
            string ffmpegPath = @"N:\unity_premium_cut\ffmpeg.exe"; //Path to ffmpeg executable
            Object[] assets1 = bundle.LoadAllAssets();
            foreach (Object ob1 in assets1)
            {
                //Character
                if (ob1.GetType().ToString() == "Spine3.Unity.Spine3SkeletonDataAsset" && ob1.name.Contains("chara"))
                {
                    skeletonAnimation = SkeletonAnimation.NewSkeletonAnimationGameObject((SkeletonDataAsset)ob1);
                    skeletonAnimation.state.SetAnimation(0, "_home", false);
                    skeletonAnimation.gameObject.GetComponent<MeshRenderer>().sortingLayerName = "back1";
                }

                //Foreground
                //if (ob1.GetType().ToString() == "Spine3.Unity.Spine3SkeletonDataAsset" && ob1.name.Contains("fg"))
                //{
                //    fgSkeletonAnimation = SkeletonAnimation.NewSkeletonAnimationGameObject((SkeletonDataAsset)ob1);
                //    fgSkeletonAnimation.state.SetAnimation(0, "_home", true);
                //    fgSkeletonAnimation.gameObject.GetComponent<MeshRenderer>().sortingLayerName = "Default";
                //}

                //Background
                //if (ob1.GetType().ToString() == "Spine3.Unity.Spine3SkeletonDataAsset" && ob1.name.Contains("bg"))
                //{
                //    bgSkeletonAnimation = SkeletonAnimation.NewSkeletonAnimationGameObject((SkeletonDataAsset)ob1);
                //    bgSkeletonAnimation.state.SetAnimation(0, "_home", true);
                //    bgSkeletonAnimation.gameObject.GetComponent<MeshRenderer>().sortingLayerName = "back2";
                //}
            }
            float[] skelTemp = { 0 };

            //Use when rendering background and/or foreground
            //bgSkeletonAnimation.skeleton.GetBounds(out float skelX, out float skelY, out float skelWidth, out float skelHeight, ref skelTemp);

            //Use when rendering character only
            skeletonAnimation.skeleton.GetBounds(out float skelX, out float skelY, out float skelWidth, out float skelHeight, ref skelTemp);

            mRenderTexture = new RenderTexture((int)skelWidth * 2, (int)skelHeight * 2, 32);
            Camera.main.targetTexture = mRenderTexture;
            Camera.main.orthographicSize = skelHeight;
            Camera.main.Render();
            GBufferRecorder recorder = Camera.main.GetComponent<GBufferRecorder>();
            recorder.isRecording = true;
            skeletonAnimation.AnimationState.Complete += delegate
            {
                recorder.isRecording = false;
                string path = recorder.outputDir.GetFullPath();
                try { Directory.CreateDirectory(path + "\\out"); } catch { }
                string[] files = Directory.EnumerateFiles(path, "Alpha_*.png").ToArray();
                int count = files.Length;
                try
                {
                    for (int i = 0; i < count + 10; i += 10)
                    {
                        string inAlphaPath = path + "\\Alpha_" + i.ToString("D4") + ".png";
                        using (MagickImage inAlphaImage = new MagickImage(inAlphaPath))
                        {
                            inAlphaImage.Trim();
                            IMagickGeometry g = inAlphaImage.Page;
                            xStack.Push(g.X);
                            yStack.Push(g.Y);
                            inAlphaImage.RePage();
                            widthStack.Push(inAlphaImage.Width);
                            heightStack.Push(inAlphaImage.Height);
                        }
                    }
                }
                catch { }
                int maxWidth = widthStack.Max();
                int maxHeight = heightStack.Max();
                int minX = xStack.Min();
                int maxX = xStack.Max();
                int minY = yStack.Min();
                int maxY = yStack.Max();
                int xDistance = maxX - minX;
                int yDistance = maxY - minY;
                int finalWidth = maxWidth + xDistance;
                int finalHeight = maxHeight + yDistance;
                if (finalWidth % 2 != 0) { finalWidth += 1; }
                if (finalHeight % 2 != 0) { finalHeight += 1; }
                IDictionary<string, MagickImage> framesToWrite = new Dictionary<string, MagickImage>();
                try
                {
                    for (int i = 0; i <= count + 10; i++)
                    {
                        string inAlphaPath = path + "\\Alpha_" + i.ToString("D4") + ".png";
                        string inPath = path + "\\FrameBuffer_" + i.ToString("D4") + ".png";
                        string outPath = path + "\\out\\out_" + i.ToString("D4") + ".png";
                        using (MagickImage inAlphaImage = new MagickImage(inAlphaPath))
                        {
                            MagickImage inImage = new MagickImage(inPath);
                            inImage.Composite(inAlphaImage, 0, 0, CompositeOperator.CopyAlpha);
                            IMagickGeometry g = new MagickGeometry();
                            g.Height = finalHeight;
                            g.Width = finalWidth;
                            g.Y = minY;
                            g.X = minX;
                            inImage.Crop(g);

                            //Use this if there is a lack of RAM (8 GB or less)
                            //inImage.Write(outPath);
                            //inImage.Dispose();

                            //Use this if there's enough RAM to go around (more than 8 GB)
                            framesToWrite.Add(outPath, inImage);
                        }
                    }
                }
                catch { }
                foreach (KeyValuePair<string, MagickImage> frame in framesToWrite)
                {
                    frame.Value.Write(frame.Key);
                }
                Process ffmpeg = new Process();
                path = path.Replace("/", "\\");
                ffmpeg.StartInfo.FileName = ffmpegPath;

                //Adjust encoding settings such as bitrate here
                ffmpeg.StartInfo.Arguments = "-f image2 -i " + path + "\\out\\out_%04d.png -c:v libvpx -pix_fmt yuva420p -b:v 2M -auto-alt-ref 0 " + path + "\\out.webm";
                
                ffmpeg.Start();
                ffmpeg.WaitForExit();

                //Clear temporary frame files
                foreach (string file in Directory.EnumerateFiles(path, "FrameBuffer_*.png"))
                {
                    File.Delete(file);
                }
                foreach (string file in Directory.EnumerateFiles(path, "Alpha_*.png"))
                {
                    File.Delete(file);
                }
                foreach (string file in Directory.EnumerateFiles(path + "\\out", "out_*.png"))
                {
                    File.Delete(file);
                }

                EditorApplication.isPlaying = false;
            };
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog(e.Message, e.ToString(), "ok", "cancel");
        }
    }
}
