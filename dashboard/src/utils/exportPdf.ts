import { jsPDF } from "jspdf";
import autoTable from "jspdf-autotable";

export function exportPdf(
  filename: string,
  title: string,
  headers: string[],
  rows: string[][],
): void {
  const doc = new jsPDF({ orientation: "landscape" });

  doc.setFontSize(16);
  doc.text(title, 14, 20);

  doc.setFontSize(9);
  doc.setTextColor(120);
  doc.text(`Generated on: ${new Date().toLocaleString()}`, 14, 28);
  doc.setTextColor(0);

  autoTable(doc, {
    startY: 34,
    head: [headers],
    body: rows,
    styles: { fontSize: 8, cellPadding: 3 },
    headStyles: { fillColor: [37, 99, 235], fontSize: 9 },
    alternateRowStyles: { fillColor: [245, 247, 250] },
    margin: { left: 14, right: 14 },
  });

  doc.save(filename.endsWith(".pdf") ? filename : `${filename}.pdf`);
}
