{{/*
Set the common labels
*/}}
{{- define "baget.labels" -}}
app: {{ .Values.fullname }}
chart: {{ .Chart.Name }}
heritage: {{ .Release.Service | quote }}
release: {{ .Release.Name | quote }}
{{- end -}}