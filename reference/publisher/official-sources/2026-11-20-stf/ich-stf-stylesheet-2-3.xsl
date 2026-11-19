<?xml version="1.0" encoding="UTF-8"?>
<!--  ICH  STF Stylesheet  (ich-stf-stylesheet-2-3.xsl) -->
<!-- 
	version 2-3 
	Correction of the stylesheet to display properly using IE9 and higher versions
	Montreal, June 2017

-->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" 
	xmlns:xlink="http://www.w3.org/1999/xlink" 
	xmlns:xlink-ectd="http://www.w3c.org/1999/xlink">

	<xsl:template match="/">
		<html>
			<head>
				<title>Study Tagging File (STF)</title>
			</head>
			<body>
				<h2>Study Tagging File (STF)</h2>
				<!-- The following table provides the study properties-->
				<table frame="hsides" width="80%">
					<tr>
						<td colspan="2">Study Title:</td>
						<td width="50%"><xsl:value-of select="//study-identifier/title"/></td>
					</tr>
					<tr>
						<td colspan="2">Study ID:</td>
						<td><xsl:value-of select="//study-identifier/study-id"/></td>
					</tr>
					<tr>
						<td colspan="3">Category:</td>
					</tr>
					<xsl:apply-templates select="//category"/>
				</table>
				<xsl:apply-templates select="//study-document"/>
			</body>
		</html>
	</xsl:template>

	<xsl:template match="category">
		<xsl:variable name="name" select="@name"/>
		<xsl:variable name="realm" select="@info-type"/>
		<xsl:variable name="value" select="."/>
		<tr>
			<xsl:if test="count(document('valid-values.xml')//category[./@name=$name]/valid-value[./@realm=$realm and ./@value=$value]) = 0">
				<xsl:attribute name="bgcolor">#FF6666</xsl:attribute>
			</xsl:if>
			<td width="10%" align="right">[<xsl:value-of select="@info-type"/>]</td>
			<td width="40%"><xsl:value-of select="@name"/></td>
			<td width="50%"><xsl:value-of select="."/></td>
		</tr>
	</xsl:template>
	
	<xsl:template match="study-document">
		<table frame="hsides" width="80%">
			<tr>
				<td colspan="4">Study Content:</td>
			</tr>
			<xsl:apply-templates select="doc-content|content-block"/>
		</table>
	</xsl:template>
	
	<xsl:template match="doc-content">
		<!--This section extracts information from the eCTD backbone file if it is located correctly at the sequence folder root-->
		<!--This section creates an xLink pointer to the eCTD backbone leaf through its ID attribute value  using  the STF file's ectd-leaf-id-value as the source for the leaf's ID attribute value-->
	    <xsl:variable name="backbone-from-stf"><xsl:value-of select="substring-before(@xlink:href, '#')"/></xsl:variable> 
		<xsl:variable name="path-from-stf" select="substring-before($backbone-from-stf, 'index.xml')"/>
		<!--This section adjusts an alternative path to STF style sheet-->
		<xsl:variable select="string-length($backbone-from-stf)" name="bbfs-len"/>
		<xsl:variable select="substring($backbone-from-stf, $bbfs-len - 13)" name="backbone-core"/>
		<xsl:variable select="concat('../../../',$backbone-core)" name="backbone-from-xsl"/>
		<xsl:variable name="doc-id" select="substring-after(@xlink:href, '#')"/>
		<xsl:variable name="doc-ref" select="document($backbone-from-xsl)//leaf[./@ID=$doc-id]/@xlink-ectd:href"/>
		<xsl:variable name="doc-title" select="document($backbone-from-xsl)//leaf[./@ID=$doc-id]/title"/>
		<xsl:variable name="path-to-file" select="concat($path-from-stf,$doc-ref)"/>
		
		<tr>
			<td valign="top" width="20%">Document Title:</td>
			<td valign="top" width="30%"><xsl:value-of select="$doc-title"/></td>
			<td valign="top" width="20%">Relative Filename:</td>
			<td valign="top" width="30%">
				<xsl:element name="a">
					<xsl:attribute name="href"><xsl:value-of select="$path-to-file"/></xsl:attribute>
					<xsl:value-of select="$doc-ref"/>
				</xsl:element>
				<br/>
			</td>
		</tr>
		<xsl:apply-templates select="property"/>
		<xsl:apply-templates select="file-tag"/>
	</xsl:template>
	
	<xsl:template match="content-block">
		<tr>
			<td colspan="4">
				<table border="1" width="60%">
					<tr>
						<td>Block Title:</td>
						<td colspan="2"><xsl:value-of select="block-title"/></td>
					</tr>
					<xsl:apply-templates select="property"/>
					<xsl:apply-templates select="file-tag"/>
					<xsl:apply-templates select="doc-content|content-block"/>
				</table>
			</td>
		</tr>
	</xsl:template>
	
	<xsl:template match="property">
		<xsl:variable name="name" select="@name"/>
		<xsl:variable name="realm" select="@info-type"/>
		<tr>
			<xsl:if test="count(document('valid-values.xml')//property/valid-value[./@realm=$realm and ./@value=$name]) = 0">
				<xsl:attribute name="bgcolor">#FF6666</xsl:attribute>
			</xsl:if>
			<td/>
			<td align="right">[<xsl:value-of select="@info-type"/>]</td>
			<td><xsl:value-of select="@name"/></td>
			<td><xsl:value-of select="."/></td>
		</tr>
	</xsl:template>
	
	<xsl:template match="file-tag">
		<xsl:variable name="name" select="@name"/>
		<xsl:variable name="realm" select="@info-type"/>
		<tr>
			<xsl:if test="count(document('valid-values.xml')//file-tag/valid-value[./@realm=$realm and ./@value=$name]) = 0">
				<xsl:attribute name="bgcolor">#FF6666</xsl:attribute>
			</xsl:if>
			<td/>
			<td align="right">[<xsl:value-of select="@info-type"/>]</td>
			<td colspan="2"><xsl:value-of select="@name"/></td>
		</tr>
	</xsl:template>
</xsl:stylesheet>
