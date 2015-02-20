using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ImapX.Enums;
using System.IO;

namespace ImapAttachementExtractor
{//
    public partial class ImapExporter : Form
    {
        bool useSsl = true;

        public ImapExporter()
        {
            InitializeComponent();

            txtHost.Text = "imap.orange.fr";
            nudPort.Value = 993;
        }

        private void btnExportFolder_Click(object sender, EventArgs e)
        {
            this.folderBrowserDialog.Description = "Selectionnez le dossier de destination :";
            if (!string.IsNullOrWhiteSpace(this.txtExportFolder.Text))
                this.folderBrowserDialog.SelectedPath = this.txtExportFolder.Text;
            if (this.folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                this.txtExportFolder.Text = this.folderBrowserDialog.SelectedPath;
        }

        private void btnTestConnection_Click(object sender, EventArgs e)
        {
            this.btnTestConnection.Enabled = false;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                using (var client = new ImapX.ImapClient(txtHost.Text, decimal.ToInt32(nudPort.Value), useSsl))
                {
                    if (!client.Connect())
                    {
                        MessageBox.Show(this, "Serveur non joignable", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    if (!client.Login(txtEmail.Text, txtPassword.Text))
                    {
                        MessageBox.Show(this, "Login ou mot de passe invalide", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    MessageBox.Show(this, "Connexion établie avec succès", "Succès", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.btnTestConnection.Enabled = true;
                this.Cursor = Cursors.Default;
            }
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            // TODO : run in a backgroundworker to work async

            if (!Directory.Exists(this.txtExportFolder.Text))
            {
                MessageBox.Show(this, "Chemin d'export invalide", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.groupBox.Enabled = false;
            this.progressBar.Visible = true;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                using (var client = new ImapX.ImapClient(txtHost.Text, decimal.ToInt32(nudPort.Value), useSsl))
                {
                    if (!client.Connect())
                    {
                        MessageBox.Show(this, "Serveur non joignable", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    if (!client.Login(txtEmail.Text, txtPassword.Text))
                    {
                        MessageBox.Show(this, "Login ou mot de passe invalide", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    this.lblStatus.Text = "Recherche des e-mails avec pièce jointe...";

                    foreach (var folder in client.Folders)
                    {
                        ExportFolder(folder);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.lblStatus.Text = string.Empty;
                this.groupBox.Enabled = true;
                this.Cursor = Cursors.Default;
                this.progressBar.Visible = false;
            }
        }

        private void ExportFolder(ImapX.Folder folder)
        {
            var folderPath = Path.Combine(txtExportFolder.Text, folder.Path);

            var lastId = GetLastId(folderPath);
            if (IsSkipped(folderPath))
                return;

            var messages = lastId == null
                ? folder.Search("ALL", MessageFetchMode.Headers)
                : folder.Search("UID " + lastId + 1 + ":*", MessageFetchMode.Headers);

            int nbMessages = messages.Length;
            this.progressBar.Maximum = nbMessages;
            int i = 1;
            foreach (var message in messages.OrderBy(x => x.UId))
            {
                try
                {
                    ExportMail(message, i, nbMessages, folderPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format("Erreur with message {0} in folder {1}\nError: {2}", message.UId, folder.Path, ex.Message), "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                i++;
            }

            foreach (var subFolder in folder.SubFolders)
                ExportFolder(subFolder);
        }

        public long? GetLastId(string folderPath)
        {
            var fileLastId = Path.Combine(folderPath, ".lastId");
            if (File.Exists(fileLastId))
            {
                var lastIdString = File.ReadAllText(fileLastId);
                long lastId;
                if (long.TryParse(lastIdString, out lastId))
                    return lastId;
                return null;
            }
            return null;
        }

        public void SetLastId(string folderPath, long lastId)
        {
            var fileLastId = Path.Combine(folderPath, ".lastId");
            File.WriteAllText(fileLastId, lastId.ToString());
        }

        public bool IsSkipped(string folderPath)
        {
            return File.Exists(Path.Combine(folderPath, ".skip"));
        }

        public void SetAsError(string folderPath, long id, string message)
        {
            File.AppendAllText(Path.Combine(folderPath, ".errors"), string.Format("{0}\t{1}{2}", id, message, Environment.NewLine));
        }

        public void ExportMail(ImapX.Message message, int current, int total, string parentPath)
        {
            GC.Collect(2);
            Application.DoEvents();
            this.lblStatus.Text = string.Format("Message '{2}' : {0}/{1}, UId = {3}", current, total, parentPath.Replace(this.txtExportFolder.Text + "\\", ""), message.UId);
            this.progressBar.Value = current;

            //var subFolder = Path.Combine(parentPath, (message.Date == null ? new DateTime(1900, 1, 1) : message.Date.Value).ToString("yyyy-MM"));
            var subFolder = parentPath;

            var lastId = GetLastId(parentPath);
            if (lastId != null && lastId > message.UId)
                return; // skip already loaded messages

            try
            {
                message.Download();
            }
            catch (OutOfMemoryException ex)
            {
                MessageBox.Show(this, ex.Message, "Fatal error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                Application.Exit();
                return;
            }
            catch (Exception ex)
            {
                //MessageBox.Show(string.Format("Error while reading:\nMessage : {0}\nsubject : {1}\nError: {2}", message.UId, message.Subject, ex.Message));
                SetAsError(parentPath, message.UId, "Read e-mail - " + ex.Message);
                return;
            }

            if (!Directory.Exists(subFolder))
                Directory.CreateDirectory(subFolder);

            var fileMailName = string.Format("{0:yyyyMMdd hhmmss} - {1} - {2}", message.Date, message.UId, message.Subject);
            foreach (var c in Path.GetInvalidPathChars())
                fileMailName = fileMailName.Replace(c, '_');
            foreach (var c in Path.GetInvalidFileNameChars())
                fileMailName = fileMailName.Replace(c, '_');
            if (fileMailName.Length > 250)
                fileMailName = fileMailName.Substring(0, 250);

            try
            {
                message.SaveTo(subFolder, fileMailName + ".eml");
            }
            catch (OutOfMemoryException ex)
            {
                MessageBox.Show(this, ex.Message, "Fatal error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                Application.Exit();
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Error while saving:\nMessage : {0}\nsubject : {1}\nError: {2}", message.UId, message.Subject, ex.Message));
                SetAsError(parentPath, message.UId, "Save e-mail - " + ex.Message);
                return;
            }

            foreach (var attachement in message.Attachments)
            {
                try
                {
                    var outputName = string.Format("{0:yyyyMMdd hhmmss} - {1} - {2}", message.Date, message.UId, attachement.FileName);
                    if (File.Exists(outputName))
                        outputName = Path.GetFileNameWithoutExtension(attachement.FileName) + "-" + Guid.NewGuid().ToString() + Path.GetExtension(attachement.FileName);
                    outputName = outputName.Replace("*", "");
                    outputName = outputName.Replace("?", "");
                    outputName = outputName.Replace("/", "_");
                    outputName = outputName.Replace("\\", "_");
                    attachement.Download();
                    attachement.Save(subFolder, outputName);
                }
                catch (OutOfMemoryException ex)
                {
                    MessageBox.Show(this, ex.Message, "Fatal error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                    Application.Exit();
                    return;
                }
                catch (Exception ex)
                {
                    //MessageBox.Show(string.Format("Error while saving:\nMessage : {0}\nsubject : {1}\nAttachement : {2}\n Error: {3}", message.UId, message.Subject, attachement.FileName, ex.Message));
                    SetAsError(parentPath, message.UId, "Save Attachement: " + attachement.FileName + " - " + ex.Message);
                    return;
                }
            }

            SetLastId(parentPath, message.UId);
        }
    }
}
