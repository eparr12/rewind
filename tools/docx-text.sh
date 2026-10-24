#!/bin/sh
# Prints a Word document's text, one paragraph per line, so git diff can show what changed in
# the .docx files in docs/ instead of "Binary files differ". Set it up once in each clone:
#   git config diff.docx.textconv "sh tools/docx-text.sh"
unzip -p "$1" word/document.xml |
  sed -e 's#</w:p>#\n#g' |
  sed -e 's/<[^>]*>//g' -e "s/&apos;/'/g" -e 's/&quot;/"/g' -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&amp;/\&/g'
