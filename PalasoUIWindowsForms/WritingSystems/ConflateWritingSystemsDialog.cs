﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Palaso.WritingSystems;

namespace Palaso.UI.WindowsForms.WritingSystems
{
	public partial class ConflateWritingSystemsDialog : Form
	{
		public ConflateWritingSystemsDialog(WritingSystemDefinition wsToConflate, IEnumerable<WritingSystemDefinition> possibleWritingSystemsToConflateWith)
		{
			InitializeComponent();
			_okButton.Click += OnOkClicked;
			_cancelButton.Click += OnCancelClicked;
			_wsSelectionComboBox.Items.AddRange(possibleWritingSystemsToConflateWith.Where(ws => ws != wsToConflate).Select(ws => new WritingSystemDisplayAdapter(ws)).ToArray());
		}

		private void OnCancelClicked(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;
			Close();
		}

		private void OnOkClicked(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		public WritingSystemDefinition WritingSystemToConflateWith
		{
			get { return ((WritingSystemDisplayAdapter) _wsSelectionComboBox.SelectedItem).WrappedWritingSystem; }
		}

		private class WritingSystemDisplayAdapter
		{
			private WritingSystemDefinition _ws ;

			public WritingSystemDisplayAdapter(WritingSystemDefinition ws)
			{
				_ws = ws;
			}

			public override string ToString()
			{
				return _ws.ListLabel;
			}

			public WritingSystemDefinition WrappedWritingSystem
			{
				get { return _ws; }
			}
		}

	}
}
