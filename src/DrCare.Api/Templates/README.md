# Dr. Care document templates

Paste the approved client wording into the HTML files in this folder. Keep the token names in double braces because the API replaces them with escaped application data.

Invoice tokens: `{{invoiceNumber}}`, `{{issuedDate}}`, `{{dueDate}}`, `{{fullName}}`, `{{email}}`, `{{productLine}}`, `{{amount}}`, `{{currency}}`.

Contract tokens: `{{contractNumber}}`, `{{fullName}}`, `{{email}}`, `{{contactNumber}}`, `{{address}}`, `{{productLine}}`, `{{listPrice}}`, `{{actualPrice}}`, `{{date}}`.

The final contract page must keep two signature areas in this order: franchisee on the left and Dr. Care on the right. The e-signature renderer stamps those two fixed A4 areas and never stores the raw signature image.
