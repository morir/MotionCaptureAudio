﻿using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using OpenNI;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;

namespace MotionCaptureAudio
{
    public partial class MainWindow : Form
    {
        #region instance fields

        private Bitmap img;

        private Context context;
        private ScriptNode scriptNode;
        private Thread readerThread;
        private bool shouldRun = true;
        private DepthGenerator depth;
        private UserGenerator userGene;
        private Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        private MotionDetector motionDetector;
        private int playerId = 0;

        private CommandState currentState = CommandState.none;
        private Dictionary<int, Dictionary<SkeletonJoint, SkeletonJointPosition>> joints = new Dictionary<int, Dictionary<SkeletonJoint, SkeletonJointPosition>>();

        private DetectionStatus detectionStatus = DetectionStatus.none;

        /// <summary>
        /// ステートです
        /// </summary>
        enum CommandState
        {
            none,
            playPausecChange,
            volumeUp,
            volumeDown,
            playerChange,
        }

        /// <summary>
        /// ユーザの検出状態
        /// </summary>
        enum DetectionStatus
        {
            none,
            detected,
            calibrated,
        }

        #endregion instance fields

        #region constructors

        public MainWindow()
        {
            InitializeComponent();

            if (!this.DesignMode)
            {
                this.img = new Bitmap(this.pictBox.Width, this.pictBox.Height);
                this.context = Context.CreateFromXmlFile(@"Config.xml", out this.scriptNode);
                this.depth = context.FindExistingNode(NodeType.Depth) as DepthGenerator;
                this.context.GlobalMirror = true;
                this.setupMotiondetector();

                this.userGene = new UserGenerator(context);
                this.userGene.NewUser += this.user_NewUser;
                this.userGene.LostUser += this.user_Lost;
                this.userGene.SkeletonCapability.CalibrationComplete += this.SkeletonCapability_CalibrationComplete;
                this.userGene.SkeletonCapability.SetSkeletonProfile(SkeletonProfile.All);

                this.context.StartGeneratingAll();
            }
        }

        #endregion constructors

        #region methods
        #endregion methods

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            new Action(this.ReaderThread).BeginInvoke(null, null);
        }

        private void setupMotiondetector()
        {
            this.motionDetector = new MotionDetector(this.depth);
            this.motionDetector.BothHandUpDetected += this.bothHandUpDetected;
            this.motionDetector.LeftHandDownDetected += this.leftHandDownDetected;
            this.motionDetector.LeftHandUpDetected += this.leftHandUpDetected;
            this.motionDetector.RightHandUpDetected += this.rightHandUpDetected;
            this.motionDetector.RightHandDownDetected += this.rightHandDownDetected;
            this.motionDetector.IdleDetected += this.idleDetected;
        }

        private void leftHandDownDetected(object sender, EventArgs e)
        {
            if (this.player.canDown[this.playerId] && this.currentState != CommandState.volumeDown)
            {
                this.player.VolumeDown(this.playerId);
                this.currentState = CommandState.volumeDown;
            }
        }

        private void bothHandUpDetected(object sender, EventArgs e)
        {
            this.shouldRun = false;

            if (this.readerThread != null)
            {
                this.readerThread.Abort();
                this.readerThread.Join();
            }

            this.player.Pause(0);
            this.player.Pause(1);
            this.player.Pause(2);

            this.Close();
        }

        private void leftHandUpDetected(object sender, EventArgs e)
        {
            if (this.player.canUp[this.playerId] && this.currentState != CommandState.volumeUp)
            {
                this.player.VolumeUp(this.playerId);
                this.currentState = CommandState.volumeUp;
            }
        }

        private void rightHandUpDetected(object sender, EventArgs e)
        {
            if (this.player.canPlay && this.currentState != CommandState.playPausecChange)
            {
                this.player.PlayPauseChange(this.playerId);
                this.currentState = CommandState.playPausecChange;
            }
        }

        private void rightHandDownDetected(object sender, EventArgs e)
        {
            if (this.player.canPlay && this.currentState != CommandState.playerChange)
            {
                this.playerId = this.playerId == 2 ? 0 : ++this.playerId;
                this.currentState = CommandState.playerChange;
                this.player.backColorChange(this.playerId);
            }
        }

        private void idleDetected(object sender, EventArgs e)
        {
            if (this.currentState != CommandState.none)
            {
                this.currentState = CommandState.none;
            }
        }

        void SkeletonCapability_CalibrationComplete(object sender, CalibrationProgressEventArgs e)
        {
            if (e.Status == CalibrationStatus.OK)
            {
                userGene.SkeletonCapability.StartTracking(e.ID);
                this.detectionStatus = DetectionStatus.calibrated;
                this.player.CalibrationCompleted(0);
                this.player.CalibrationCompleted(1);
                this.player.CalibrationCompleted(2);
            }
        }

        void user_NewUser(object sender, NewUserEventArgs e)
        {
            Console.WriteLine(String.Format("ユーザ検出: {0}", e.ID));
            if (this.detectionStatus == DetectionStatus.none)
            {
                this.detectionStatus = DetectionStatus.detected;
                userGene.SkeletonCapability.RequestCalibration(e.ID, true);

                this.player.DetectedUser(0);
                this.player.DetectedUser(1);
                this.player.DetectedUser(2);
            }
        }

        private void user_Lost(object sender, UserLostEventArgs e)
        {
            Console.WriteLine(String.Format("ユーザ消失: {0}", e.ID));
            this.detectionStatus = DetectionStatus.none;

            this.player.LostUser(0);
            this.player.LostUser(1);
            this.player.LostUser(2);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }

        private void ReaderThread()
        {
            while (this.shouldRun)
            {
                this.context.WaitAndUpdateAll();

                this.dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    int[] users = userGene.GetUsers();
                    foreach (int user in users)
                    {
                        if (!userGene.SkeletonCapability.IsTracking(user)) continue;

                        var pointDict = new Dictionary<SkeletonJoint, SkeletonJointPosition>();
                        foreach (SkeletonJoint skeletonJoint in Enum.GetValues(typeof(SkeletonJoint)))
                        {
                            if (!userGene.SkeletonCapability.IsJointAvailable(skeletonJoint)) continue;
                            pointDict.Add(skeletonJoint, userGene.SkeletonCapability.GetSkeletonJointPosition(user, skeletonJoint));
                        }

                        this.motionDetector.DetectMotion(user, pointDict);
                        var pointDic = new List<Object>() { user, pointDict };

                        this.Invoke(new Action<int,Dictionary<SkeletonJoint, SkeletonJointPosition>>(drawSkeleton), pointDic.ToArray());
                        this.pictBox.Invalidate();
                    }
                }));
            }
        }

        private void drawSkeleton(int user, Dictionary<SkeletonJoint, SkeletonJointPosition> pointDict)
        {
            Graphics g = Graphics.FromImage(this.img);
            Color color = this.detectionStatus == DetectionStatus.calibrated ? Color.Red : Color.Gray;

            g.FillRectangle(Brushes.Black, g.VisibleClipBounds);

            DrawLine(g, color, pointDict, SkeletonJoint.Head, SkeletonJoint.Neck);

            DrawLine(g, color, pointDict, SkeletonJoint.LeftShoulder, SkeletonJoint.RightHip);
            DrawLine(g, color, pointDict, SkeletonJoint.RightShoulder, SkeletonJoint.LeftHip);

            DrawLine(g, color, pointDict, SkeletonJoint.LeftShoulder, SkeletonJoint.LeftElbow);
            DrawLine(g, color, pointDict, SkeletonJoint.LeftElbow, SkeletonJoint.LeftHand);

            DrawLine(g, color, pointDict, SkeletonJoint.LeftShoulder, SkeletonJoint.RightShoulder);
            DrawLine(g, color, pointDict, SkeletonJoint.RightShoulder, SkeletonJoint.RightElbow);
            DrawLine(g, color, pointDict, SkeletonJoint.RightElbow, SkeletonJoint.RightHand);

            DrawLine(g, color, pointDict, SkeletonJoint.LeftHip, SkeletonJoint.RightHip);
            DrawLine(g, color, pointDict, SkeletonJoint.LeftHip, SkeletonJoint.LeftKnee);
            DrawLine(g, color, pointDict, SkeletonJoint.RightHip, SkeletonJoint.RightKnee);

            DrawLine(g, color, pointDict, SkeletonJoint.LeftKnee, SkeletonJoint.LeftFoot);
            DrawLine(g, color, pointDict, SkeletonJoint.RightKnee, SkeletonJoint.RightFoot);

            this.pictBox.BackgroundImage = this.img;

            g.Dispose(); 
        }

        private void GetJoint(int user, SkeletonJoint joint)
        {
            SkeletonJointPosition pos = this.userGene.SkeletonCapability.GetSkeletonJointPosition(user, joint);
            if (pos.Position.Z == 0)
            {
                pos.Confidence = 0;
            }
            else
            {
                pos.Position = this.depth.ConvertRealWorldToProjective(pos.Position);
            }
            this.joints[user][joint] = pos;
        }

        private void DrawLine(Graphics g, Color color, Dictionary<SkeletonJoint, SkeletonJointPosition> dict, SkeletonJoint j1, SkeletonJoint j2)
        {
            Point3D pos1 = this.depth.ConvertRealWorldToProjective(dict[j1].Position);
            Point3D pos2 = this.depth.ConvertRealWorldToProjective(dict[j2].Position);

            if (dict[j1].Confidence == 0 || dict[j2].Confidence == 0) return;

            g.DrawLine(new Pen(color, 30),
                        new Point((int)(pos1.X * 3), (int)(pos1.Y * 3)),
                        new Point((int)(pos2.X * 3), (int)(pos2.Y * 3)));

            this.pictBox.Invalidate();
        }
    }
}