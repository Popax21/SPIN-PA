#!/bin/sh -e
cat SPIN.prefix Celeste\ Spinner\ Analysis\ -\ the\ SPIN\ theory.md | grep -v '^{}{}' | pandoc --shift-heading-level-by=-1 --pdf-engine=xelatex -f markdown -t pdf -o SPIN.pdf
