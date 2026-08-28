using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.ML;
using Emgu.CV.ML.Structure;
using Emgu.CV.Structure;
using Emgu.CV.UI;
using Emgu.Util;
using Modbus;
using Modbus.Device;
using NAudio.Wave;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Media;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
//using System.Threading.Tasks;
using System.Windows.Forms;
using tesseract;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
//sing WMPLib;


namespace Auto_parking
{
    public partial class MainForm : Form
    {
        string strCon = @"Data Source=DESKTOP-5PC5CI9\SQLEXPRESS;Initial Catalog=CarPark;Integrated Security=True;TrustServerCertificate=True
";
        SqlConnection SqlCon = null;
        SqlDataAdapter adapter = new SqlDataAdapter();
        DataTable table = new DataTable();
        private readonly System.Windows.Forms.Button[] _parkingButtons;

        

        // create modbus master
        IModbusSerialMaster master;
        public MainForm()
        {
            InitializeComponent();
            _parkingButtons = new System.Windows.Forms.Button[]{
                Btn_Pic1, Btn_Pic2, Btn_Pic3, Btn_Pic4,
                Btn_Pic5, Btn_Pic6, Btn_Pic7, Btn_Pic8,
                Btn_Pic9,Btn_Pic10,Btn_Pic11,Btn_Pic12,
            };
        }


        #region Define
        List<Image<Bgr, byte>> PlateImagesList = new List<Image<Bgr, byte>>();
        Image Plate_Draw;
        List<string> PlateTextList = new List<string>();
        List<Rectangle> listRect = new List<Rectangle>();
        PictureBox[] box = new PictureBox[12];

        public TesseractProcessor full_tesseract = null;
        public TesseractProcessor ch_tesseract = null;
        public TesseractProcessor num_tesseract = null;
        private string m_path = Application.StartupPath + @"\data\";
        private List<string> lstimages = new List<string>();
        private const string m_lang = "eng";

        //int current = 0;
        Capture capture = null;
        #endregion

        ImageForm IF;
        private void MainForm_Load(object sender, EventArgs e)
        {
            
            try
            {
                capture = new Emgu.CV.Capture();
            }
            catch { }

            timer1.Enabled = true;

            IF = new ImageForm();

            full_tesseract = new TesseractProcessor();
            bool succeed = full_tesseract.Init(m_path, m_lang, 3);
            if (!succeed)
            {
                MessageBox.Show("Tesseract initialization failed. The application will exit.");
                Application.Exit();
            }
            full_tesseract.SetVariable("tessedit_char_whitelist", "ABCDEFHKLMNPRSTVXY1234567890").ToString();

            ch_tesseract = new TesseractProcessor();
            succeed = ch_tesseract.Init(m_path, m_lang, 3);
            if (!succeed)
            {
                MessageBox.Show("Tesseract initialization failed. The application will exit.");
                Application.Exit();
            }
            ch_tesseract.SetVariable("tessedit_char_whitelist", "ABCDEFHKLMNPRSTUVXY").ToString();

            num_tesseract = new TesseractProcessor();
            succeed = num_tesseract.Init(m_path, m_lang, 3);
            if (!succeed)
            {
                MessageBox.Show("Tesseract initialization failed. The application will exit.");
                Application.Exit();
            }
            num_tesseract.SetVariable("tessedit_char_whitelist", "1234567890").ToString();


            m_path = System.Environment.CurrentDirectory + "\\";
            string[] ports = SerialPort.GetPortNames();
            for (int i = 0; i < box.Length; i++)
            {
                box[i] = new PictureBox();
            }

        }
        private void debug_btn_Click(object sender, EventArgs e)
        {
            if (IF.Visible == false)
            {
                IF.Show();
            }
            else
            {
                IF.Hide();
            }
        }
        bool success = true;
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (success == true)
            {
                success = false;
                new Thread(() =>
                {
                    try
                    {
                        capture.SetCaptureProperty(CAP_PROP.CV_CAP_PROP_FRAME_WIDTH, 640);
                        capture.SetCaptureProperty(CAP_PROP.CV_CAP_PROP_FRAME_HEIGHT, 480);
                        Image<Bgr, byte> cap = capture.QueryFrame();
                        if (cap != null)
                        {
                            MethodInvoker mi = delegate
                            {
                                try
                                {
                                    Bitmap bmp = cap.ToBitmap();
                                    pictureBox_WC.Image = bmp;
                                    IF.pictureBox4.Image = bmp;
                                    pictureBox_WC.Update();
                                    IF.pictureBox4.Update();
                                }
                                catch (Exception ex)
                                { }
                            };
                            if (InvokeRequired)
                                Invoke(mi);
                        }
                    }
                    catch (Exception) { }
                    success = true;
                }).Start();

            }
        }

        public void ProcessImage(string urlImage)
        {
            PlateImagesList.Clear();
            PlateTextList.Clear();
            FileStream fs = new FileStream(urlImage, FileMode.Open, FileAccess.Read);
            Image img = Image.FromStream(fs);
            Bitmap image = new Bitmap(img);
            //pictureBox2.Image = image;
            IF.pictureBox2.Image = image;
            fs.Close();

            FindLicensePlate4(image, out Plate_Draw);

        }
        public static Bitmap RotateImage(Image image, float angle)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            PointF offset = new PointF((float)image.Width / 2, (float)image.Height / 2);

            //create a new empty bitmap to hold rotated image
            Bitmap rotatedBmp = new Bitmap(image.Width, image.Height);
            rotatedBmp.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            //make a graphics object from the empty bitmap
            Graphics g = Graphics.FromImage(rotatedBmp);

            //Put the rotation point in the center of the image
            g.TranslateTransform(offset.X, offset.Y);

            //rotate the image
            g.RotateTransform(angle);

            //move the image back
            g.TranslateTransform(-offset.X, -offset.Y);

            //draw passed in image onto graphics object
            g.DrawImage(image, new PointF(0, 0));

            return rotatedBmp;
        }

        private string Ocr(Bitmap image_s, bool isFull, bool isNum = false)
        {
            string temp = "";
            Image<Gray, byte> src = new Image<Gray, byte>(image_s);
            double ratio = 1;
            while (true)
            {
                ratio = (double)CvInvoke.cvCountNonZero(src) / (src.Width * src.Height);
                if (ratio > 0.5) break;
                src = src.Dilate(2);
            }
            Bitmap image = src.ToBitmap();

            TesseractProcessor ocr;
            if (isFull)
                ocr = full_tesseract;
            else if (isNum)
                ocr = num_tesseract;
            else
                ocr = ch_tesseract;

            int cou = 0;
            ocr.Clear();
            ocr.ClearAdaptiveClassifier();
            temp = ocr.Apply(image);
            while (temp.Length > 3)
            {
                Image<Gray, byte> temp2 = new Image<Gray, byte>(image);
                temp2 = temp2.Erode(2);
                image = temp2.ToBitmap();
                ocr.Clear();
                ocr.ClearAdaptiveClassifier();
                temp = ocr.Apply(image);
                cou++;
                if (cou > 10)
                {
                    temp = "";
                    break;
                }
            }
            return temp;

        }

        public void FindLicensePlate2(Bitmap image)
        {
            if (image == null)
                return;
            Bitmap src;
            Image dst = image;
            Image<Bgr, byte> frame_b = null;
            Image<Bgr, byte> plate_b = null;
            double sum_b = 0;
            for (float i = -45; i <= 45; i = i + 5)
            {
                src = RotateImage(dst, i);
                PlateImagesList.Clear();
                Image<Bgr, byte> frame = new Image<Bgr, byte>(src);
                using (Image<Gray, byte> grayframe = new Image<Gray, byte>(src))
                {


                    var faces =
                           grayframe.DetectHaarCascade(
                                   new HaarCascade(Application.StartupPath + "\\output-hv-33-x25.xml"), 1.1, 8,
                                   HAAR_DETECTION_TYPE.DO_CANNY_PRUNING,
                                   new Size(0, 0)
                                   )[0];
                    foreach (var face in faces)
                    {
                        Image<Bgr, byte> tmp = frame.Copy();
                        tmp.ROI = face.rect;

                        frame.Draw(face.rect, new Bgr(Color.Blue), 2);

                        PlateImagesList.Add(tmp.Resize(500, 500, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC, true));


                    }

                }
                if (PlateImagesList.Count != 0)
                {
                    Image<Gray, byte> gr = new Image<Gray, byte>(PlateImagesList[0].Resize(100, 100, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR).ToBitmap());
                    Gray cannyThreshold = new Gray(gr.GetAverage().Intensity);
                    Gray cannyThresholdLinking = new Gray(gr.GetAverage().Intensity);
                    Image<Gray, byte> cannyEdges = gr.Canny(cannyThreshold, cannyThresholdLinking);

                    double sum = 0;
                    for (int j = 0; j < cannyEdges.Height - 1; j++)
                    {
                        for (int k = 0; k < cannyEdges.Width - 1; k++)
                        {
                            if (j < 20 || j > 180 || k < 20 || k > 180)
                            {
                                sum += cannyEdges.Data[j, k, 0]; // tính tổng các điểm trắng ở viền ngoài
                            }
                            //else
                            //{
                            //    cannyEdges.Data[j, k, 0] = 0;
                            //}
                        }
                    }
                    //pictureBox4.Image = cannyEdges.ToBitmap();
                    //pictureBox4.Update();
                    if (sum_b == 0 || sum > sum_b)
                    {
                        frame_b = frame.Clone();
                        plate_b = PlateImagesList[0].Resize(400, 400, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR).Clone();
                        sum_b = sum;
                    }
                }

            }
            if (plate_b != null)
            {
                PlateImagesList.Add(plate_b);
                pictureBox_WC.Image = frame_b.ToBitmap();
                pictureBox_WC.Update();
            }

        }
        public void FindLicensePlate(Bitmap image, out Image plateDraw)
        {
            plateDraw = null;
            Image<Bgr, byte> frame = new Image<Bgr, byte>(image);
            bool isface = false;
            using (Image<Gray, byte> grayframe = new Image<Gray, byte>(image))
            {


                var faces =
                       grayframe.DetectHaarCascade(
                               new HaarCascade(Application.StartupPath + "\\output-hv-33-x25.xml"), 1.1, 8,
                               HAAR_DETECTION_TYPE.DO_CANNY_PRUNING,
                               new Size(0, 0)
                               )[0];
                foreach (var face in faces)
                {
                    Image<Bgr, byte> tmp = frame.Copy();
                    tmp.ROI = face.rect;

                    frame.Draw(face.rect, new Bgr(Color.Blue), 2);

                    PlateImagesList.Add(tmp);

                    isface = true;
                }
                if (isface)
                {
                    Image<Bgr, byte> showimg = frame.Clone();
                    plateDraw = (Image)showimg.ToBitmap();
                    //showimg = frame.Resize(imageBox1.Width, imageBox1.Height, 0);
                    //pictureBox1.Image = showimg.ToBitmap();
                    IF.pictureBox2.Image = showimg.ToBitmap();
                    if (PlateImagesList.Count > 1)
                    {
                        for (int i = 1; i < PlateImagesList.Count; i++)
                        {
                            if (PlateImagesList[0].Width < PlateImagesList[i].Width)
                            {
                                PlateImagesList[0] = PlateImagesList[i];
                            }
                        }
                    }
                    PlateImagesList[0] = PlateImagesList[0].Resize(400, 400, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR);
                }


            }
        }
        public void FindLicensePlate4(Bitmap image, out Image plateDraw)
        {
            plateDraw = null;
            Image<Bgr, byte> frame;
            bool isface = false;
            Bitmap src;
            //pictureBox2.Image = new Image<Gray, byte>(image).ToBitmap();
            Image dst = image;
            HaarCascade haar = new HaarCascade(Application.StartupPath + "\\output-hv-33-x25.xml");
            for (float i = 0; i <= 20; i = i + 3)
            {
                for (float s = -1; s <= 1 && s + i != 1; s += 2)
                {
                    src = RotateImage(dst, i * s);
                    PlateImagesList.Clear();
                    frame = new Image<Bgr, byte>(src);
                    using (Image<Gray, byte> grayframe = new Image<Gray, byte>(src))
                    {
                        var faces =
                       grayframe.DetectHaarCascade(haar, 1.1, 8, HAAR_DETECTION_TYPE.DO_CANNY_PRUNING, new Size(0, 0))[0];
                        foreach (var face in faces)
                        {
                            Image<Bgr, byte> tmp = frame.Copy();
                            tmp.ROI = face.rect;

                            frame.Draw(face.rect, new Bgr(Color.Blue), 2);

                            PlateImagesList.Add(tmp);

                            isface = true;
                        }
                        if (isface)
                        {
                            Image<Bgr, byte> showimg = frame.Clone();
                            plateDraw = (Image)showimg.ToBitmap();
                            //showimg = frame.Resize(imageBox1.Width, imageBox1.Height, 0);
                            //pictureBox1.Image = showimg.ToBitmap();
                            IF.pictureBox2.Image = showimg.ToBitmap();
                            if (PlateImagesList.Count > 1)
                            {
                                for (int k = 1; k < PlateImagesList.Count; k++)
                                {
                                    if (PlateImagesList[0].Width < PlateImagesList[k].Width)
                                    {
                                        PlateImagesList[0] = PlateImagesList[k];
                                    }
                                }
                            }
                            PlateImagesList[0] = PlateImagesList[0].Resize(400, 400, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR);
                            return;
                        }


                    }
                }
            }


        }
        public void FindLicensePlate3(Bitmap image)
        {
            if (image == null)
                return;
            Bitmap src;
            Image dst = image;
            Image<Bgr, byte> frame_b = null;
            Image<Bgr, byte> plate_b = null;
            double sum_b = 1000;
            HaarCascade haar = new HaarCascade(Application.StartupPath + "\\output-hv-33-x25.xml");
            for (float i = 0; i <= 35; i = i + 3)
            {
                for (float s = -1; s <= 1 && s + i != 1; s += 2)
                {
                    src = RotateImage(dst, i * s);
                    PlateImagesList.Clear();
                    Image<Bgr, byte> frame = new Image<Bgr, byte>(src);
                    using (Image<Gray, byte> grayframe = new Image<Gray, byte>(src))
                    {


                        var faces = grayframe.DetectHaarCascade(haar, 1.1, 8, HAAR_DETECTION_TYPE.DO_CANNY_PRUNING, new Size(0, 0))[0];
                        foreach (var face in faces)
                        {
                            Image<Bgr, byte> tmp = frame.Copy();
                            tmp.ROI = face.rect;

                            frame.Draw(face.rect, new Bgr(Color.Blue), 2);

                            PlateImagesList.Add(tmp.Resize(400, 400, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC));

                            //imageBox1.Image = tmp;
                            //imageBox1.Update();

                        }
                        //Image<Bgr, Byte> showimg = new Image<Bgr, Byte>(image.Size);
                        //showimg = frame.Resize(imageBox1.Width, imageBox1.Height, 0);
                        //pictureBox1.Image = grayframe.ToBitmap();
                    }
                    if (PlateImagesList.Count != 0)
                    {
                        Image<Gray, byte> src2 = new Image<Gray, byte>(PlateImagesList[0].ToBitmap());
                        double thr = src2.GetAverage().Intensity;

                        double min = 0, max = 255;
                        if (thr - 50 > 0)
                        {
                            min = thr - 50;
                        }
                        if (thr + 50 < 255)
                        {
                            max = thr + 50;
                        }
                        for (double value = min; value <= max; value += 5)
                        {
                            src2 = new Image<Gray, byte>(PlateImagesList[0].ToBitmap());
                            int c = 0;
                            List<Rectangle> listR = new List<Rectangle>();
                            using (MemStorage storage = new MemStorage())
                            {
                                src2 = src2.ThresholdBinary(new Gray(value), new Gray(255));
                                Contour<Point> contours = src2.FindContours(Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_SIMPLE, Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_LIST, storage);
                                while (contours != null)
                                {

                                    Rectangle rect = contours.BoundingRectangle;
                                    double ratio = (double)rect.Width / rect.Height;
                                    if (rect.Width > 20 && rect.Width < 150
                                        && rect.Height > 80 && rect.Height < 180
                                        && ratio > 0.2 && ratio < 1.1)
                                    {
                                        c++;
                                        listR.Add(contours.BoundingRectangle);
                                    }
                                    contours = contours.HNext;
                                }
                            }
                            double sum = 1000;
                            if (c >= 2)
                            {
                                for (int u = 0; u < c; u++)
                                {
                                    for (int v = u + 1; v < c; v++)
                                    {
                                        if (Math.Abs(listR[u].Y - listR[v].Y) < sum)
                                        {

                                            sum = Math.Abs(listR[u].Y - listR[v].Y);
                                            if (sum < 4)
                                            {
                                                PlateImagesList.Add(PlateImagesList[0].Resize(400, 400, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR).Clone());
                                                pictureBox_CarOut.Image = frame.ToBitmap();
                                                pictureBox_CarOut.Update();
                                                return;
                                            }
                                        }
                                    }
                                }

                            }

                            if (sum < sum_b)
                            {
                                frame_b = frame.Clone();
                                plate_b = PlateImagesList[0].Resize(400, 400, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR).Clone();
                                sum_b = sum;
                            }
                        }
                    }
                }


            }
            if (plate_b != null)
            {
                PlateImagesList.Add(plate_b);
                pictureBox_CarOut.Image = frame_b.ToBitmap();
                pictureBox_CarOut.Update();
            }

        }

        private void Reconize(string link, out Image hinhbienso, out string bienso, out string bienso_text)
        {
            for (int i = 0; i < box.Length; i++)
            {
                this.Controls.Remove(box[i]);
            }

            hinhbienso = null;
            bienso = "";
            bienso_text = "";
            ProcessImage(link);
            if (PlateImagesList.Count != 0)
            {
                Image<Bgr, byte> src = new Image<Bgr, byte>(PlateImagesList[0].ToBitmap());
                Bitmap grayframe;
                FindContours con = new FindContours();
                Bitmap color;
                int c = con.IdentifyContours(src.ToBitmap(), 50, false, out grayframe, out color, out listRect);
                //int z = con.count;
                pictureBox_PlateIn.Image = color;
                IF.pictureBox1.Image = color;
                hinhbienso = Plate_Draw;
                pictureBox_PlateOut.Image = grayframe;
                IF.pictureBox3.Image = grayframe;
                //textBox2.Text = c.ToString();
                Image<Gray, byte> dst = new Image<Gray, byte>(grayframe);
                //dst = dst.Dilate(2);
                //dst = dst.Erode(3);
                grayframe = dst.ToBitmap();
                //pictureBox2.Image = grayframe.Clone(listRect[2], grayframe.PixelFormat);
                string zz = "";

                // lọc và sắp xếp số
                List<Bitmap> bmp = new List<Bitmap>();
                List<int> erode = new List<int>();
                List<Rectangle> up = new List<Rectangle>();
                List<Rectangle> dow = new List<Rectangle>();
                int up_y = 0, dow_y = 0;
                bool flag_up = false;

                int di = 0;

                if (listRect == null) return;

                for (int i = 0; i < listRect.Count; i++)
                {
                    Bitmap ch = grayframe.Clone(listRect[i], grayframe.PixelFormat);
                    int cou = 0;
                    full_tesseract.Clear();
                    full_tesseract.ClearAdaptiveClassifier();
                    string temp = full_tesseract.Apply(ch);
                    while (temp.Length > 3)
                    {
                        Image<Gray, byte> temp2 = new Image<Gray, byte>(ch);
                        temp2 = temp2.Erode(2);
                        ch = temp2.ToBitmap();
                        full_tesseract.Clear();
                        full_tesseract.ClearAdaptiveClassifier();
                        temp = full_tesseract.Apply(ch);
                        cou++;
                        if (cou > 10)
                        {
                            listRect.RemoveAt(i);
                            i--;
                            di = 0;
                            break;
                        }
                        di = cou;
                    }
                }

                for (int i = 0; i < listRect.Count; i++)
                {
                    for (int j = i; j < listRect.Count; j++)
                    {
                        if (listRect[i].Y > listRect[j].Y + 100)
                        {
                            flag_up = true;
                            up_y = listRect[j].Y;
                            dow_y = listRect[i].Y;
                            break;
                        }
                        else if (listRect[j].Y > listRect[i].Y + 100)
                        {
                            flag_up = true;
                            up_y = listRect[i].Y;
                            dow_y = listRect[j].Y;
                            break;
                        }
                        if (flag_up == true) break;
                    }
                }

                for (int i = 0; i < listRect.Count; i++)
                {
                    if (listRect[i].Y < up_y + 50 && listRect[i].Y > up_y - 50)
                    {
                        up.Add(listRect[i]);
                    }
                    else if (listRect[i].Y < dow_y + 50 && listRect[i].Y > dow_y - 50)
                    {
                        dow.Add(listRect[i]);
                    }
                }

                if (flag_up == false) dow = listRect;

                for (int i = 0; i < up.Count; i++)
                {
                    for (int j = i; j < up.Count; j++)
                    {
                        if (up[i].X > up[j].X)
                        {
                            Rectangle w = up[i];
                            up[i] = up[j];
                            up[j] = w;
                        }
                    }
                }
                for (int i = 0; i < dow.Count; i++)
                {
                    for (int j = i; j < dow.Count; j++)
                    {
                        if (dow[i].X > dow[j].X)
                        {
                            Rectangle w = dow[i];
                            dow[i] = dow[j];
                            dow[j] = w;
                        }
                    }
                }

                int x = 12;
                int c_x = 0;

                for (int i = 0; i < up.Count; i++)
                {
                    Bitmap ch = grayframe.Clone(up[i], grayframe.PixelFormat);
                    Bitmap o = ch;
                    //ch = con.Erodetion(ch);
                    string temp;
                    if (i < 2)
                    {
                        temp = Ocr(ch, false, true); // nhan dien so
                    }
                    else
                    {
                        temp = Ocr(ch, false, false);// nhan dien chu
                    }

                    zz += temp;
                    box[i].Location = new Point(x + i * 50, 290);
                    box[i].Size = new Size(50, 100);
                    box[i].SizeMode = PictureBoxSizeMode.StretchImage;
                    box[i].Image = ch;
                    box[i].Update();
                    //this.Controls.Add(box[i]);
                    IF.Controls.Add(box[i]);
                    c_x++;
                }
                zz += "\r\n";
                for (int i = 0; i < dow.Count; i++)
                {
                    Bitmap ch = grayframe.Clone(dow[i], grayframe.PixelFormat);
                    //ch = con.Erodetion(ch);
                    string temp = Ocr(ch, false, true); // nhan dien so
                    zz += temp;
                    box[i + c_x].Location = new Point(x + i * 50, 390);
                    box[i + c_x].Size = new Size(50, 100);
                    box[i + c_x].SizeMode = PictureBoxSizeMode.StretchImage;
                    box[i + c_x].Image = ch;
                    box[i + c_x].Update();
                    //this.Controls.Add(box[i + c_x]);
                    IF.Controls.Add(box[i + c_x]);
                }
                bienso = zz.Replace("\n", "");
                bienso = bienso.Replace("\r", "");
                IF.textBox6.Text = zz;
                bienso_text = zz;

            }
        }

        private void regonizeBtn_Click(object sender, EventArgs e)
        {
            //while (true) ;
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "Image (*.bmp; *.jpg; *.jpeg; *.png) |*.bmp; *.jpg; *.jpeg; *.png|All files (*.*)|*.*||";
            dlg.InitialDirectory = Application.StartupPath + "\\ImageTest";
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.Cancel)
            {
                return;
            }
            string startupPath = dlg.FileName;

            Image temp1;
            string temp2, temp3;
            Reconize(startupPath, out temp1, out temp2, out temp3);
            pictureBox_CarIn.Image = temp1;
            if (temp3 == "")
                text_PlateIn.Text = "ko nhận dạng dc biển số";
            else
                text_PlateIn.Text = temp3;
        }

        private void capCameraBtn_Click(object sender, EventArgs e)
        {
            if (capture != null)
            {
                timer1.Enabled = false;
                pictureBox_CarOut.Image = null;
                IF.pictureBox2.Image = null;
                capture.QueryFrame().Save("aa.bmp");
                FileStream fs = new FileStream(m_path + "aa.bmp", FileMode.Open, FileAccess.Read);
                Image temp = Image.FromStream(fs);
                fs.Close();
                pictureBox_CarOut.Image = temp;
                IF.pictureBox2.Image = temp;
                pictureBox_CarOut.Update();
                IF.pictureBox2.Update();
                Image temp1;
                string temp2, temp3;
                Reconize(m_path + "aa.bmp", out temp1, out temp2, out temp3);
                pictureBox_CarIn.Image = temp1;
                if (temp3 == "")
                    text_PlateIn.Text = "ko nhận dạng dc biển số";
                else
                    text_PlateIn.Text = temp3;
                string textDoc = text_PlateIn.Text;
                string textNgang = textDoc.Replace("\r\n", "")
                                 .Replace("\n", "")
                                 .Replace("\r", "")
                                 .Replace(" ", "");

                // Gán vào Label
                Txt_Numcar.Text = textNgang;
                timer1.Enabled = true;
            }
            DateTime currentTime = DateTime.Now;

            // Định dạng thời gian theo ý muốn
            string timeString = currentTime.ToString("HH:mm:ss dd/MM/yyyy");

            // Hiển thị thời gian vào TextBox
            Txt_time.Text = timeString;
        }

        #region WEBCAM
        WEBCAM[] cam = new WEBCAM[3];
        private void pictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                PictureBox p = (PictureBox)sender;
                for (int i = 0; i < cam.Length; i++)
                {
                    if (cam[i] != null && cam[i].status == "run" && cam[i].pb == p.Name)
                    {
                        cam[i].Stop();
                        cam[i] = null;
                    }
                }
                ContextMenu m = new ContextMenu();
                List<string> ls = WEBCAM.get_all_cam();
                for (int i = 0; i <= 2 & i < ls.Count; i++)
                {
                    m.MenuItems.Add(ls[i], (s, e2) =>
                    {
                        MenuItem menuItem = s as MenuItem;
                        ContextMenu owner = menuItem.Parent as ContextMenu;
                        PictureBox pb = (PictureBox)owner.SourceControl;
                        if (cam[menuItem.Index] != null && cam[menuItem.Index].status == "run")
                        {
                            cam[menuItem.Index].Stop();
                            //cam[menuItem.Index] = null;
                        }
                        cam[menuItem.Index] = new WEBCAM();
                        cam[menuItem.Index].Start(menuItem.Index);
                        cam[menuItem.Index].put_picturebox(pb.Name);
                    });
                }
                m.Show(p, new Point(e.X, e.Y));
            }
        }
        private void timer3_Tick(object sender, EventArgs e)
        {
            try
            {
                for (int i = 0; i < cam.Length; i++)
                {
                    if (cam[i] != null && cam[i].status == "run" && cam[i].image != null)
                    {
                        MethodInvoker mi = delegate
                        {
                            PictureBox pb = this.Controls.Find(cam[i].pb, true).FirstOrDefault() as PictureBox;
                            pb.Image = cam[i].image;
                            pb.Update();
                            pb.Invalidate();
                        };
                        if (InvokeRequired)
                        {
                            Invoke(mi);
                            return;
                        }

                        PictureBox pb2 = this.Controls.Find(cam[i].pb, true).FirstOrDefault() as PictureBox;
                        pb2.Image = cam[i].image;
                        pb2.Update();
                        pb2.Invalidate();
                    }
                }
            }
            catch (Exception) { }
        }

        #endregion

        private void realtimeText_TextChanged(object sender, EventArgs e)
        {
            realtimeText.Text = DateTime.Now.ToLongTimeString();
        }

        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void pic19_Click(object sender, EventArgs e)
        {

        }

        private void Btn_Pic1_Click(object sender, EventArgs e)
        {
            if (Pic1.Visible == false)
            {
                
                master.WriteSingleCoil(1, 201, true);
                master.WriteSingleCoil(1, 100, true);
                Pic1.Visible = true;
                Pic13.Visible = false;
                
            }
            else
            {
                //Txt_Status.Text = "OUT";
               // button2.PerformClick();
                Pic1.Visible = false;
                Pic13.Visible = true;
                //Btn_Pic1.Text = "rong";
                master.WriteSingleCoil(1, 201, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic1.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
            {
                connection.Open();

                // 📌 Lấy dữ liệu trước khi xóa
                string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 1";
                int parkingId = 0;
                string carNumber = "", cardId = "";

                using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                {
                    using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            parkingId = Convert.ToInt32(reader["id"]);
                            carNumber = reader["id_car"].ToString();
                            cardId = reader["card_number"].ToString();
                        }
                        else
                        {
                            MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                }

                // 📌 Lưu dữ liệu vào `parking_history`
                string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                {
                    historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                    historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                    historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                    historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                    historyCmd.ExecuteNonQuery();
                }

                // 📌 Cập nhật dữ liệu thay vì xóa dòng
                string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 1";
                using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                {
                    updateCmd.ExecuteNonQuery();
                }

                // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

        }
        }

        private void Btn_Pic2_Click(object sender, EventArgs e)
        {
            if (Pic2.Visible == false)
            {
                master.WriteSingleCoil(1, 202, true);
                master.WriteSingleCoil(1, 100, true);
                Pic2.Visible = true;
                Pic14.Visible = false;
                //Btn_Pic2.Text = "BIENSO";
            }
            else
            {
                //Txt_Status.Text = "OUT";
                //button2.PerformClick();
                master.WriteSingleCoil(1, 202, true);
                master.WriteSingleCoil(1, 101, true);
                Pic2.Visible = false;
                Pic14.Visible = true;
                Btn_Pic2.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 2";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 2";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

            }
        }

        private void btn_Pic3_Click(object sender, EventArgs e)
        {
            if (Pic3.Visible == false)
            {
                Pic3.Visible = true;
                Pic15.Visible = false;
                //Btn_Pic3.Text = "BIENSO";
                master.WriteSingleCoil(1, 203, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic3.Visible = false;
                Pic15.Visible = true;
                master.WriteSingleCoil(1, 203, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic3.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 3";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 3";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

            }
        }

        private void Btn_Pic4_Click(object sender, EventArgs e)
        {
            if (Pic4.Visible == false)
            {
                Pic4.Visible = true;
                Pic16.Visible = false;
                //Btn_Pic4.Text = "BIENSO";
                master.WriteSingleCoil(1, 204, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic4.Visible = false;
                Pic16.Visible = true;
                master.WriteSingleCoil(1, 204, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic4.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 4";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 4";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic5_Click(object sender, EventArgs e)
        {
            if (Pic5.Visible == false)
            {
                Pic5.Visible = true;
                Pic17.Visible = false;
                master.WriteSingleCoil(1, 205, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic5.Visible = false;
                Pic17.Visible = true;
                master.WriteSingleCoil(1, 205, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic5.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 5";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 5";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic6_Click(object sender, EventArgs e)
        {
            if (Pic6.Visible == false)
            {
                Pic6.Visible = true;
                Pic18.Visible = false;
                master.WriteSingleCoil(1, 206, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic6.Visible = false;
                Pic18.Visible = true;
                master.WriteSingleCoil(1, 206, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic6.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 6";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 6";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic7_Click(object sender, EventArgs e)
        {
            if (Pic7.Visible == false)
            {
                Pic7.Visible = true;
                Pic19.Visible = false;
                master.WriteSingleCoil(1, 207, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic7.Visible = false;
                Pic19.Visible = true;
                master.WriteSingleCoil(1, 207, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic7.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 7";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 7";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic8_Click(object sender, EventArgs e)
        {
            if (Pic8.Visible == false)
            {
                Pic8.Visible = true;
                Pic20.Visible = false;
                master.WriteSingleCoil(1, 208, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic8.Visible = false;
                Pic20.Visible = true;
                master.WriteSingleCoil(1, 208, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic8.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 8";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 8";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic9_Click(object sender, EventArgs e)
        {
            if (Pic9.Visible == false)
            {
                Pic9.Visible = true;
                Pic21.Visible = false;
                master.WriteSingleCoil(1, 209, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic9.Visible = false;
                Pic21.Visible = true;
                master.WriteSingleCoil(1, 209, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic9.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 9";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 9";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic_Click(object sender, EventArgs e)
        {
            if (Pic10.Visible == false)
            {
                Pic10.Visible = true;
                Pic22.Visible = false;
                master.WriteSingleCoil(1, 210, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic10.Visible = false;
                Pic22.Visible = true;
                master.WriteSingleCoil(1, 210, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic10.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 10";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 10";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic11_Click(object sender, EventArgs e)
        {
            if (Pic11.Visible == false)
            {
                Pic11.Visible = true;
                Pic23.Visible = false;
                master.WriteSingleCoil(1, 211, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic11.Visible = false;
                Pic23.Visible = true;
                master.WriteSingleCoil(1, 211, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic11.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 11";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 11";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Btn_Pic12_Click(object sender, EventArgs e)
        {
            if (Pic12.Visible == false)
            {
                Pic12.Visible = true;
                Pic24.Visible = false;
                master.WriteSingleCoil(1, 212, true);
                master.WriteSingleCoil(1, 100, true);
            }
            else
            {
                Pic12.Visible = false;
                Pic24.Visible = true;
                master.WriteSingleCoil(1, 212, true);
                master.WriteSingleCoil(1, 101, true);
                Btn_Pic12.Text = "";
                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // 📌 Lấy dữ liệu trước khi xóa
                    string carInfoQuery = "SELECT id, id_car, card_number FROM parking WHERE id = 12";
                    int parkingId = 0;
                    string carNumber = "", cardId = "";

                    using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                    {
                        using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parkingId = Convert.ToInt32(reader["id"]);
                                carNumber = reader["id_car"].ToString();
                                cardId = reader["card_number"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }

                    // 📌 Lưu dữ liệu vào `parking_history`
                    string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                                VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                    using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                    {
                        historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = parkingId;
                        historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                        historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                        historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                        historyCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật dữ liệu thay vì xóa dòng
                    string updateQuery = @"UPDATE parking 
                              SET id_car = NULL, 
                                  card_number = NULL, 
                                  is_parking = 0, 
                                  time = NULL 
                              WHERE id = 12";
                    using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                    {
                        updateCmd.ExecuteNonQuery();
                    }

                    // 📌 Cập nhật giao diện sau khi xóa dữ liệu
                    Btn_Pic1.Enabled = false; // Vô hiệu hóa nút sau khi xe rời đi
                    RefreshParkingListView(); // Load lại danh sách xe sau khi xóa

                    //MessageBox.Show("Đã cập nhật lịch sử và xóa dữ liệu tại ID = 1!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        private System.Windows.Forms.Button AssignValueToEmptyButton(string value)
        {
            for (int i = 1; i <= 12; i++)
            {
                if (this.Controls.Find($"Btn_Pic{i}", true).FirstOrDefault() is System.Windows.Forms.Button btn
                    && string.IsNullOrEmpty(btn.Text))
                {
                    btn.Text = value;
                    btn.Tag = Txt_ID; // Gán giá trị vào Tag

                    // Hiệu ứng visual
                    btn.BackColor = Color.LightBlue;
                    btn.ForeColor = Color.DarkBlue;

                    return btn;
                }
            }

            MessageBox.Show("Tất cả Button đã có nội dung!");
            return null;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // Kiểm tra vị trí hợp lệ
            if (string.IsNullOrEmpty(Txt_loca.Text) || !int.TryParse(Txt_loca.Text, out int locationId))
            {
                MessageBox.Show("Vui lòng nhập vị trí hợp lệ!", "Cảnh báo",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Txt_loca.Focus();
                return;
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    // Kiểm tra bản ghi có tồn tại không
                    string checkQuery = "SELECT COUNT(*) FROM parking WHERE id = @id";
                    using (SqlCommand checkCmd = new SqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.Add("@id", SqlDbType.Int).Value = locationId;
                        int exists = (int)checkCmd.ExecuteScalar();

                        if (exists == 0)
                        {
                            MessageBox.Show($"Không tìm thấy bản ghi với ID = {locationId}", "Cảnh báo",
                                          MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }

                    if (Txt_Status.Text.Trim() == "IN")
                    {

                        // Xử lý khi là IN (UPDATE)
                        string updateQuery = @"UPDATE parking 
                                    SET id_car = @bien, 
                                        card_number = @id, 
                                        is_parking = 1, 
                                        time = @tg 
                                    WHERE id = @vt";

                        using (SqlCommand updateCmd = new SqlCommand(updateQuery, connection))
                        {
                            updateCmd.Parameters.Add("@vt", SqlDbType.Int).Value = locationId;
                            updateCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = Txt_Numcar.Text.Trim();
                            updateCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = Txt_ID.Text.Trim(); // Giữ nguyên định dạng
                            updateCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                            int rowsAffected = updateCmd.ExecuteNonQuery();

                            string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking,time)
                            VALUES (@p_id, @bien, @id, 'IN', @tg)";
                            using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                            {
                                historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = locationId;
                                historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = Txt_Numcar.Text.Trim();
                                historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = Txt_ID.Text.Trim();
                                historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                                historyCmd.ExecuteNonQuery();
                            }

                            if (rowsAffected > 0)
                            {
                                //MessageBox.Show($"Đã cập nhật thành công bản ghi ID = {locationId}", "Thành công",
                                              //MessageBoxButtons.OK, MessageBoxIcon.Information);
                                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carin.mp3");

                            }
                        }
                    }
                    else if (Txt_Status.Text.Trim() == "OUT")
                    {
                        // Xử lý khi là OUT (XÓA dữ liệu, chỉ giữ vị trí)
                        string clearQuery = @"UPDATE parking 
                                    SET id_car = NULL, 
                                        card_number = NULL, 
                                        is_parking = 0, 
                                        time = NULL 
                                    WHERE id = @vt";

                        string carInfoQuery = "SELECT id_car, card_number FROM parking WHERE id = @id";
                        string carNumber = "", cardId = "";

                        using (SqlCommand carInfoCmd = new SqlCommand(carInfoQuery, connection))
                        {
                            carInfoCmd.Parameters.Add("@id", SqlDbType.Int).Value = locationId;
                            using (SqlDataReader reader = carInfoCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    carNumber = reader["id_car"] == DBNull.Value ? "" : reader["id_car"].ToString();
                                    cardId = reader["card_number"] == DBNull.Value ? "" : reader["card_number"].ToString();
                                }
                            }
                        }

                        // Lưu dữ liệu cũ vào `parking_history`
                        string historyQuery = @"INSERT INTO parking_history (id, id_car, card_number, is_parking, time)
                        VALUES (@p_id, @bien, @id, 'OUT', @tg)";
                        using (SqlCommand historyCmd = new SqlCommand(historyQuery, connection))
                        {
                            historyCmd.Parameters.Add("@p_id", SqlDbType.Int).Value = locationId;
                            historyCmd.Parameters.Add("@bien", SqlDbType.NVarChar, 20).Value = carNumber;
                            historyCmd.Parameters.Add("@id", SqlDbType.NVarChar, 50).Value = cardId;
                            historyCmd.Parameters.Add("@tg", SqlDbType.DateTime).Value = DateTime.Now;

                            historyCmd.ExecuteNonQuery();
                        }

                        // Sau khi lưu lịch sử, tiếp tục xóa dữ liệu trên bảng `parking`

                        using (SqlCommand clearCmd = new SqlCommand(clearQuery, connection))
                        {
                            clearCmd.Parameters.Add("@vt", SqlDbType.Int).Value = locationId;
                            int rowsAffected = clearCmd.ExecuteNonQuery();

                            if (rowsAffected > 0)
                            {
                                //MessageBox.Show($"Đã xóa dữ liệu bản ghi ID = {locationId}", "Thành công",
                                //MessageBoxButtons.OK, MessageBoxIcon.Information);
                                PhatAmThanhNAudio(@"D:\do_an\Project\Project\LPR_share\carout.mp3");

                            }
                        }
                    }
                    // Tự động nhấn nút tương ứng với vị trí
                    PressCorrespondingButton(locationId);

                    RefreshParkingListView();
                }
            }
            catch (SqlException ex)
            {
                MessageBox.Show($"Lỗi SQL: {ex.Message}", "Lỗi database",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi hệ thống",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void RefreshParkingListView()
        {
            listView1.Items.Clear();


            try
            {
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();
                    string query = "SELECT id, id_car, card_number, is_parking, time FROM parking ORDER BY id";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Xử lý ID vị trí
                                string id = reader["id"] == DBNull.Value ? "" : reader["id"].ToString();

                                // Xử lý biển số xe
                                string idCar = reader["id_car"] == DBNull.Value ? "" : reader["id_car"].ToString();

                                // Xử lý ID thẻ - giữ nguyên định dạng số 0 đầu
                                string cardNumber = reader["card_number"] == DBNull.Value ? "" :
                                                 string.Format("{0:D8}", Convert.ToInt64(reader["card_number"]));

                                // Xử lý trạng thái
                                bool isParking = reader["is_parking"] != DBNull.Value && Convert.ToBoolean(reader["is_parking"]);

                                // Xử lý thời gian
                                string timeStr = "";
                                if (reader["time"] != DBNull.Value)
                                {
                                    DateTime timeValue = Convert.ToDateTime(reader["time"]);
                                    timeStr = timeValue.ToString("dd/MM/yyyy HH:mm:ss");
                                }

                                // Tạo và thêm item vào ListView
                                ListViewItem item = new ListViewItem(id);
                                item.SubItems.Add(idCar);
                                item.SubItems.Add(cardNumber);
                                item.SubItems.Add(isParking ? "IN" : "OUT");
                                item.SubItems.Add(timeStr);

                                listView1.Items.Add(item);
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                MessageBox.Show($"Lỗi SQL khi tải dữ liệu: {sqlEx.Message}", "Lỗi Database",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi tải dữ liệu: {ex.Message}", "Lỗi",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void PressCorrespondingButton(int locationId)
        {
            ReadD100();
            // Tạo mảng các nút tương ứng với vị trí
            System.Windows.Forms.Button[] positionButtons = { Btn_Pic1, Btn_Pic2, Btn_Pic3, Btn_Pic4,
                       Btn_Pic5, Btn_Pic6, Btn_Pic7, Btn_Pic8, Btn_Pic9,
                        Btn_Pic10,Btn_Pic11,Btn_Pic12};

            // Kiểm tra locationId hợp lệ
            if (locationId >= 1 && locationId <= positionButtons.Length)
            {
                // Lấy nút tương ứng
                System.Windows.Forms.Button btn = positionButtons[locationId - 1];

                // Thay đổi màu sắc để hiển thị trạng thái
                if (Txt_Status.Text.Trim() == "IN")
                {
                    btn.BackColor = Color.LightGreen;  // Màu khi có xe
                    btn.Text = Txt_Numcar.Text;       // Hiển thị biển số xe

                }
                else
                {
                    btn.BackColor = SystemColors.Control; // Màu mặc định
                    btn.Text = "";                      // Xóa biển số

                }

                // Kích hoạt sự kiện Click của nút
                btn.Enabled = true;
                btn.PerformClick();
                Txt_Numcar.Clear();
                Txt_ID.Clear();
                Txt_Status.Clear();
                Txt_time.Clear();
                Txt_loca.Clear();
                if(button2.BackColor == Color.Green)
                {
                    btn.Enabled = false;
                }    
                
            }
        }
        private void Btn_conn_Click(object sender, EventArgs e)
        {
                
            //serialPort2.Open();

            // create modbus master
            

            byte slaveId = 1;
            ushort startAddress = 1;
            ushort numRegisters = 5;
            // Xóa dữ liệu cũ trong ListView (nếu có)
            listView1.Items.Clear();

            // Thiết lập các cột cho ListView (nếu chưa có)
            if (listView1.Columns.Count == 0)
            {
                listView1.View = View.Details;
                listView1.Columns.Add("Vị Trí", 100);
                listView1.Columns.Add("Biển Số", 150);
                listView1.Columns.Add("ID Thẻ", 100);
                listView1.Columns.Add("Trạng Thái", 100);
                listView1.Columns.Add("Thời Gian", 150);
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(strCon))
                {
                    connection.Open();

                    string query = "SELECT [id], [id_car], [card_number], [is_parking], [time] FROM [parking]";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ListViewItem item = new ListViewItem(reader["id"]?.ToString() ?? "");
                                item.SubItems.Add(reader["id_car"]?.ToString() ?? "");
                                item.SubItems.Add(reader["card_number"]?.ToString() ?? "");
                                item.SubItems.Add(reader["is_parking"]?.ToString() ?? "");
                                item.SubItems.Add(reader["Time"]?.ToString() ?? "");
                                listView1.Items.Add(item);
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                string errorDetails = "";
                foreach (SqlError error in sqlEx.Errors)
                {
                    errorDetails += $"Lỗi SQL {error.Number}: {error.Message}\n";
                }
                MessageBox.Show($"Lỗi SQL chi tiết:\n{errorDetails}", "Lỗi database", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi hệ thống: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                              "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            try
            {
                if (SqlCon == null)
                {
                    SqlCon = new SqlConnection(strCon);
                }
                if (SqlCon.State == ConnectionState.Closed)
                {
                    SqlCon.Open();
                    MessageBox.Show("Kết nối thành công!");

                    // Sau khi kết nối thành công, hiển thị dữ liệu lên các Button
                    DisplayParkingCars();
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi kết nối: " + ex.Message);
            }
        }

        private void DisplayParkingCars()
        {
            try
            {
                // Lấy tất cả các xe đang đỗ, sắp xếp theo id
                string query = "SELECT id, id_car FROM parking WHERE is_parking = 1 AND id_car IS NOT NULL ORDER BY id";

                using (SqlCommand command = new SqlCommand(query, SqlCon))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        // Reset tất cả các button và PictureBox
                        for (int i = 0; i < _parkingButtons.Length; i++)
                        {
                            _parkingButtons[i].Invoke((MethodInvoker)delegate {
                                _parkingButtons[i].Text = "";
                                _parkingButtons[i].BackColor = SystemColors.Control;
                            });

                            // Ẩn tất cả các PictureBox liên quan
                            var picHide = this.Controls.Find($"Pic{i + 1}", true).FirstOrDefault();
                            var picShow = this.Controls.Find($"Pic{i + 13}", true).FirstOrDefault();

                            if (picHide != null) picHide.Visible = false;
                            if (picShow != null) picShow.Visible = true;
                        }

                        // Cập nhật trạng thái biển số xe và PictureBox tương ứng
                        while (reader.Read())
                        {
                            int parkingId = reader.GetInt32(reader.GetOrdinal("id"));
                            string carId = reader["id_car"].ToString();

                            // Kiểm tra nếu id nằm trong phạm vi các button
                            if (parkingId > 0 && parkingId <= _parkingButtons.Length)
                            {
                                int buttonIndex = parkingId - 1; // Vì id bắt đầu từ 1, còn mảng từ 0

                                _parkingButtons[buttonIndex].Invoke((MethodInvoker)delegate {
                                    _parkingButtons[buttonIndex].Text = carId;
                                    _parkingButtons[buttonIndex].BackColor = Color.LightGreen;
                                });

                                // Ẩn PictureBox hiện tại và hiển thị PictureBox mới
                                var picHide = this.Controls.Find($"Pic{parkingId}", true).FirstOrDefault();
                                var picShow = this.Controls.Find($"Pic{parkingId + 12}", true).FirstOrDefault();

                                if (picHide != null) picHide.Visible = true;
                                if (picShow != null) picShow.Visible = false;
                               
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tải dữ liệu: " + ex.Message);
            }


            if (Btn_conn.BackColor != Color.Red) // Kết nối
            {
                try
                {
                    Btn_conn.Text = "CONNECTED";
                    //Btn_conn.BackColor = Color.Red;

                    if (!serialPort1.IsOpen)
                    {
                        serialPort2.PortName = "COM8";
                        serialPort2.BaudRate = Convert.ToInt32("9600");
                        serialPort2.Open();
                        master = ModbusSerialMaster.CreateRtu(serialPort2);
                        string ID = "";
                        serialPort1.PortName = "COM7";
                        serialPort1.BaudRate = Convert.ToInt32("112500");
                        serialPort1.Open();
                        serialPort2.BaudRate = 9600;
                        serialPort2.DataBits = 8;
                        serialPort2.Parity = Parity.None;
                        serialPort2.StopBits = StopBits.One;
                        ena();
                    }

                    // Bắt đầu đọc liên tục
                    serialPort1.DataReceived += SerialPort_DataReceived;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi kết nối: " + ex.Message);
                    Btn_conn.Text = "CONNECT";
                    Btn_conn.BackColor = Color.Green;
                }
            }
            else // Ngắt kết nối
{
                DialogResult cc = MessageBox.Show("Bạn muốn ngắt kết nối với hệ thống?",
                                               "Xác nhận ngắt kết nối",
                                               MessageBoxButtons.YesNo,
                                               MessageBoxIcon.Question);

                if (cc == DialogResult.Yes)
                {
                    try
                    {
                        // Đóng kết nối database
                        if (SqlCon != null && SqlCon.State == ConnectionState.Open)
                        {
                            SqlCon.Close();
                        }

                        // Đóng cổng serial và hủy đăng ký sự kiện
                        if (serialPort1 != null)
                        {
                            serialPort1.DataReceived -= SerialPort_DataReceived;
                            if (serialPort1.IsOpen) serialPort1.Close();
                        }

                        if (serialPort2 != null)
                        {
                            if (serialPort2.IsOpen) serialPort2.Close();
                            master = null; // Giải phóng Modbus master
                        }

                        // Cập nhật giao diện
                        Btn_conn.Text = "CONNECT";
                        Btn_conn.BackColor = Color.Green;

                        MessageBox.Show("Đã ngắt kết nối thành công!", "Thông báo",
                                      MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi khi ngắt kết nối: " + ex.Message, "Lỗi",
                                      MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                } 
            } 
        }
            

        private string lastID = "";
        //private string lastID = ""; // Lưu ID cuối cùng để so sánh

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string newID = serialPort1.ReadLine().Trim();

                this.Invoke((MethodInvoker)delegate
                {
                    // Chỉ xử lý nếu ID mới khác ID trước đó
                    if (newID != lastID)
                    {
                        Txt_ID.Text = newID;
                        lastID = "";
                        capCameraBtn.PerformClick(); // Chụp hình mỗi khi có thẻ mới
                        ProcessListView();
                    }

                });
            }
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    MessageBox.Show("Lỗi đọc dữ liệu: " + ex.Message);
                });
            }
            
        }
        private void ProcessListView()
        {
            string searchValue = Txt_ID.Text.Trim();
            if (string.IsNullOrEmpty(searchValue))
            {
                MessageBox.Show("Vui lòng nhập ID cần tìm!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int emptyIndex = -1;
            int foundIndex = -1;
            int minDifference = int.MaxValue;

            foreach (ListViewItem item in listView1.Items)
            {
                string columnValue = item.SubItems[2].Text; // Cột thứ 3 (index 2)

                // So sánh giá trị nhập vào với giá trị từng dòng
                if (columnValue.Equals(searchValue, StringComparison.OrdinalIgnoreCase))
                {
                    foundIndex = item.Index;
                    int number = foundIndex+1;
                    Txt_Status.Text = "OUT";
                    Txt_loca.Text = Convert.ToString(number); 
                    //item.BackColor = System.Drawing.Color.Yellow;
                    return;
                }
                 if(string.IsNullOrEmpty(columnValue) && emptyIndex == -1)
        {
                    emptyIndex = item.Index;
                }
            }

            // Nếu không tìm thấy giá trị trùng, hiển thị dòng có ô rỗng đầu tiên
            if (foundIndex == -1)
            {
                if (emptyIndex != -1)
                {
                    int number = emptyIndex + 1;
                    Txt_loca.Text = Convert.ToString(number);
                    Txt_Status.Text = "IN";
                    
                }
                else
                {
                    Txt_loca.Text = "Không tìm thấy ô rỗng!";
                }
            }
           

        }
        //}


        private void Txt_ID_TextChanged(object sender, EventArgs e)
        {
           
            
        }

        private void Btn_Mode_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Bạn có chắc chắn muốn thoát không?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                Application.Exit();
            }
        }
        private void PhatAmThanhNAudio(string duongDanFile)
        {
            var audioFile = new AudioFileReader(duongDanFile);
            var outputDevice = new WaveOutEvent();
            outputDevice.Init(audioFile);
            outputDevice.Play();
        }
        private void ena()
        {
            //InitModbus();
            StartReadingD100();
            if (master == null || !serialPort2.IsOpen)
            {
                MessageBox.Show("Modbus chưa kết nối!");
                return;
            }

            try
            {
                
                byte slaveId = 1;
                ushort startAddress = 100; // Địa chỉ Modbus của D100 (0x0064 hex = 100 dec)
                ushort numRegisters = 1;

                // Đọc giá trị từ D100
                ushort[] registers = master.ReadHoldingRegisters(slaveId, startAddress, numRegisters);
                textBox1.Text = registers[0].ToString();
                // Dùng Invoke để cập nhật UI từ luồng an toàn
                this.Invoke((MethodInvoker)delegate
                {
                    if (registers[0] == 1)
                    {
                        button2.BackColor = Color.White;
                        button2.Enabled = false;
                    }
                    else
                    {
                        button2.BackColor = Color.Green;
                        button2.Enabled = true;
                        
                    }
                });
            }
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    MessageBox.Show($"Lỗi đọc Modbus: {ex.Message}");
                });
            }
        }
        private System.Windows.Forms.Timer modbusTimer;
        private void StartReadingD100()
        {
            modbusTimer = new System.Windows.Forms.Timer();
            modbusTimer.Interval = 1000; // Đọc mỗi 1 giây
            modbusTimer.Tick += (s, e) => ReadD100();
            modbusTimer.Start();
        }

        private void ReadD100()
        {
            if (master == null || !serialPort2.IsOpen) return;

            try
            {
                ushort[] registers = master.ReadHoldingRegisters(1, 0x0064, 1);
                this.Invoke((MethodInvoker)delegate
                {
                    string enab = registers[0].ToString();
                    if( enab == "1")
                    {
                        textBox1.Text = "chờ";
                        button2.BackColor = Color.White;
                        button2.Enabled = false;
                    }
                    else
                    {
                        textBox1.Text = "sẵn sàng";
                        button2.BackColor = Color.Green;
                        button2.Enabled = true;

                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi: {ex.Message}");
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (textBox2.Text == "3241")
            {
                label2.BackColor = Color.Lime;
                label2.Text = "MANUAL";
                Btn_Pic1.Enabled = true;
                Btn_Pic2.Enabled = true;
                Btn_Pic3.Enabled = true;
                Btn_Pic4.Enabled = true;
                Btn_Pic5.Enabled = true;
                Btn_Pic6.Enabled = true;
                Btn_Pic7.Enabled = true;
                Btn_Pic8.Enabled = true;
                Btn_Pic9.Enabled = true;
                Btn_Pic10.Enabled = true;
                Btn_Pic11.Enabled = true;
                Btn_Pic12.Enabled = true;
                textBox2.Clear();
            }
            else
            {
                label2.BackColor = Color.DodgerBlue;
                label2.Text = "AUTO";
                Btn_Pic1.Enabled = false;
                Btn_Pic2.Enabled = false;
                Btn_Pic3.Enabled = false;
                Btn_Pic4.Enabled = false;
                Btn_Pic5.Enabled = false;
                Btn_Pic6.Enabled = false;
                Btn_Pic7.Enabled = false;
                Btn_Pic8.Enabled = false;
                Btn_Pic9.Enabled = false;
                Btn_Pic10.Enabled = false;
                Btn_Pic11.Enabled = false;
                Btn_Pic12.Enabled = false;
            }
        }

        private void History_Click(object sender, EventArgs e)
        {
            if( History.Text == "HISTORY")
            {
                listView2.Visible = true;  // Hiển thị ListView2
                LoadParkingHistory();
                History.Text = "CLOSE";
            }
            else
            {
                listView2.Visible = false;
                History.Text = "HISTORY";
            } 
            
            
        }
        private void LoadParkingHistory()
        {
            listView2.Items.Clear(); // Xóa dữ liệu cũ trước khi tải mới

            using (SqlConnection connection = new SqlConnection(strCon))
            {
                connection.Open();
                string query = "SELECT id, id_car, card_number, is_parking, time FROM parking_history ORDER BY time DESC";

                using (SqlCommand cmd = new SqlCommand(query, connection))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ListViewItem item = new ListViewItem(reader["id"].ToString());
                        item.SubItems.Add(reader["id_car"].ToString());
                        item.SubItems.Add(reader["card_number"].ToString());
                        item.SubItems.Add(reader["is_parking"].ToString());
                        item.SubItems.Add(Convert.ToDateTime(reader["time"]).ToString("yyyy-MM-dd HH:mm:ss"));

                        listView2.Items.Add(item);
                    }
                }
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            master.WriteSingleCoil(1, 102, true);
        }
    }
}